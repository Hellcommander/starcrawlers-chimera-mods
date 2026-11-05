using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace Breachwork.Titles
{
    /// <summary>
    /// The title archive (.bwt): stored class titles in their own compressed format, so titles kept for many class
    /// combinations take little disk. Shared by the loader (SCCL_titles.bwt, and any mod's *.bwt) and the Class Titler
    /// (Mods/YourClassTitles/titles.bwt), which compiles this same file. No Unity or game types.
    ///
    /// What it holds: class sets, each with one or more titles, and optional per-character records. A set is its class
    /// ids sorted, joined with '+' (case doesn't matter), the same key the loader's ClassTitles.Signature makes.
    ///
    /// How it packs, inside a Deflate stream:
    /// - every class id once, in a table; every title word once, most used first, so common words cost one byte;
    /// - a set of k classes is one number, its rank among all k-class sets of the table (the combinatorial number
    ///   system: sum of C(place_i, i) over its sorted places), and sets are written in rank order as the gap from the
    ///   one before, so a well-filled archive spends two or three bytes per set; past 1000 classes or 7 per set (where the
    ///   rank would overflow 64 bits) a group falls back to the places' own gaps;
    /// - a title is its word count and its words' places in the word table;
    /// - every number is a LEB128 varint.
    /// The file: "BWTA1", the payload's length (uint32) and FNV-1a 64 hash (uint64), then the deflated payload. A changed
    /// or damaged file fails the hash and is refused rather than read wrong.
    /// </summary>
    public sealed class TitleArchive
    {
        public sealed class Record
        {
            public string name;
            public int roll;
            /// <summary>A class id, or null.</summary>
            public string lead;
            /// <summary>A set key that is in <see cref="Sets"/>, or null.</summary>
            public string set;
            public int pick = -1;
        }

        public readonly Dictionary<string, List<string>> Sets = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        public readonly List<Record> Records = new List<Record>();

        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("BWTA1");
        private const int MaxRankClasses = 1000, MaxRankSize = 7;

        public static string Key(IEnumerable<string> ids) =>
            string.Join("+", ids.Where(x => !string.IsNullOrEmpty(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase));

        // ---- writing ----------------------------------------------------------------------------------------------

        public byte[] ToBytes()
        {
            var sets = Sets.Where(kv => kv.Value != null && kv.Value.Any(t => !string.IsNullOrEmpty(t)))
                .ToDictionary(kv => Key(kv.Key.Split('+')), kv => kv.Value.Where(t => !string.IsNullOrEmpty(t)).ToList(), StringComparer.OrdinalIgnoreCase);
            var classes = sets.Keys.SelectMany(k => k.Split('+')).Concat(Records.Select(r => r.lead).Where(x => !string.IsNullOrEmpty(x)))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            var place = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < classes.Count; i++) place[classes[i]] = i;

            var count = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string t in sets.Values.SelectMany(x => x))
                foreach (string w in Words(t)) count[w] = (count.TryGetValue(w, out int n) ? n : 0) + 1;
            var words = count.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key).ToList();
            var wordAt = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < words.Count; i++) wordAt[words[i]] = i;

            var body = new MemoryStream();
            var w_ = new BinaryWriter(body, Encoding.UTF8);
            Uint(w_, (ulong)classes.Count);
            foreach (string c in classes) Str(w_, c);
            Uint(w_, (ulong)words.Count);
            foreach (string w in words) Str(w_, w);

            // sets by size; each size in rank order (or by places, when it can't rank)
            var order = new List<string>();
            var bySize = sets.Keys.Select(k => k.Split('+').Select(id => place[id]).OrderBy(i => i).ToArray()).GroupBy(p => p.Length).OrderBy(g => g.Key).ToList();
            Uint(w_, (ulong)bySize.Count);
            foreach (var g in bySize)
            {
                int k = g.Key;
                bool rank = classes.Count <= MaxRankClasses && k <= MaxRankSize;
                Uint(w_, (ulong)k);
                w_.Write((byte)(rank ? 0 : 1));
                var list = rank ? g.OrderBy(p => Rank(p)).ToList() : g.OrderBy(p => string.Join(",", p.Select(i => i.ToString("D6")))).ToList();
                Uint(w_, (ulong)list.Count);
                ulong last = 0;
                foreach (int[] p in list)
                {
                    if (rank) { ulong r = Rank(p); Uint(w_, r - last); last = r; }
                    else { int prev = -1; foreach (int i in p) { Uint(w_, (ulong)(i - prev - 1)); prev = i; } }
                    string key = string.Join("+", p.Select(i => classes[i]));
                    order.Add(key);
                    List<string> titles = sets[key];
                    Uint(w_, (ulong)titles.Count);
                    foreach (string t in titles)
                    {
                        var ws = Words(t);
                        Uint(w_, (ulong)ws.Count);
                        foreach (string w in ws) Uint(w_, (ulong)wordAt[w]);
                    }
                }
            }
            var setRef = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < order.Count; i++) setRef[order[i]] = i;
            var recs = Records.Where(r => !string.IsNullOrEmpty(r.name)).ToList();
            Uint(w_, (ulong)recs.Count);
            foreach (Record r in recs)
            {
                Str(w_, r.name);
                Uint(w_, Zig(r.roll));
                Uint(w_, string.IsNullOrEmpty(r.lead) ? 0UL : (ulong)place[r.lead] + 1);
                string s = r.set == null ? null : Key(r.set.Split('+'));
                Uint(w_, s != null && setRef.TryGetValue(s, out int at) ? (ulong)at + 1 : 0UL);
                Uint(w_, Zig(r.pick));
            }
            w_.Flush();
            byte[] payload = body.ToArray();

            var file = new MemoryStream();
            file.Write(Magic, 0, Magic.Length);
            var fw = new BinaryWriter(file);
            fw.Write((uint)payload.Length);
            fw.Write(Fnv(payload));
            fw.Flush();
            using (var z = new DeflateStream(file, CompressionLevel.Optimal, true)) z.Write(payload, 0, payload.Length);
            return file.ToArray();
        }

        /// <summary>Writes to a temporary file, reads it back, then puts it in place, so a failed write never costs the old file.</summary>
        public void Save(string path)
        {
            byte[] bytes = ToBytes();
            FromBytes(bytes); // refuses a file it couldn't read back
            string tmp = path + ".tmp";
            File.WriteAllBytes(tmp, bytes);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        // ---- reading ----------------------------------------------------------------------------------------------

        public static TitleArchive Load(string path) => FromBytes(File.ReadAllBytes(path));

        public static TitleArchive FromBytes(byte[] bytes)
        {
            if (bytes.Length < Magic.Length + 12 || !bytes.Take(Magic.Length).SequenceEqual(Magic)) throw new InvalidDataException("not a title archive (BWTA1)");
            uint length = BitConverter.ToUInt32(bytes, Magic.Length);
            ulong hash = BitConverter.ToUInt64(bytes, Magic.Length + 4);
            if (length > 512u << 20) throw new InvalidDataException("its length is not believable");
            var payload = new byte[length];
            using (var z = new DeflateStream(new MemoryStream(bytes, Magic.Length + 12, bytes.Length - Magic.Length - 12), CompressionMode.Decompress))
            {
                int got = 0, n;
                while (got < payload.Length && (n = z.Read(payload, got, payload.Length - got)) > 0) got += n;
                if (got != payload.Length) throw new InvalidDataException("it is cut short");
            }
            if (Fnv(payload) != hash) throw new InvalidDataException("its hash does not match: the file was changed or damaged");

            var a = new TitleArchive();
            var r = new BinaryReader(new MemoryStream(payload), Encoding.UTF8);
            var classes = new string[Count(r)];
            for (int i = 0; i < classes.Length; i++) classes[i] = r.ReadString();
            var words = new string[Count(r)];
            for (int i = 0; i < words.Length; i++) words[i] = r.ReadString();
            var order = new List<string>();
            int groups = Count(r);
            for (int g = 0; g < groups; g++)
            {
                int k = Count(r);
                bool rank = r.ReadByte() == 0;
                int n = Count(r);
                ulong last = 0;
                for (int e = 0; e < n; e++)
                {
                    int[] p;
                    if (rank) { last += Uint(r); p = Unrank(last, k); }
                    else { p = new int[k]; int prev = -1; for (int i = 0; i < k; i++) p[i] = prev = prev + 1 + Count(r); }
                    if (p.Any(i => i < 0 || i >= classes.Length)) throw new InvalidDataException("a set names a class past the table");
                    string key = string.Join("+", p.Select(i => classes[i]));
                    int tc = Count(r);
                    var titles = new List<string>(tc);
                    for (int t = 0; t < tc; t++)
                    {
                        int wc = Count(r);
                        var sb = new StringBuilder();
                        for (int w = 0; w < wc; w++)
                        {
                            int at = Count(r);
                            if (at >= words.Length) throw new InvalidDataException("a title names a word past the table");
                            if (w > 0) sb.Append(' ');
                            sb.Append(words[at]);
                        }
                        titles.Add(sb.ToString());
                    }
                    a.Sets[key] = titles;
                    order.Add(key);
                }
            }
            int recs = Count(r);
            for (int i = 0; i < recs; i++)
            {
                var rec = new Record { name = r.ReadString(), roll = Unzig(Uint(r)) };
                ulong lead = Uint(r), set = Uint(r);
                rec.lead = lead == 0 || lead > (ulong)classes.Length ? null : classes[lead - 1];
                rec.set = set == 0 || set > (ulong)order.Count ? null : order[(int)set - 1];
                rec.pick = Unzig(Uint(r));
                a.Records.Add(rec);
            }
            return a;
        }

        // ---- pieces -----------------------------------------------------------------------------------------------

        /// <summary>A title's words; one with odd spacing is kept whole, so every title reads back exactly.</summary>
        private static List<string> Words(string title)
        {
            var ws = title.Split(' ').ToList();
            return ws.All(w => w != "") ? ws : new List<string> { title };
        }

        private static ulong Choose(int n, int k)
        {
            if (k < 0 || n < k) return 0;
            ulong c = 1;
            for (int i = 1; i <= k; i++) c = c * (ulong)(n - k + i) / (ulong)i; // exact at every step
            return c;
        }

        /// <summary>The set's rank among all sets of its size: sum of C(p_i, i+1) over its sorted places.</summary>
        private static ulong Rank(int[] sorted)
        {
            ulong r = 0;
            for (int i = 0; i < sorted.Length; i++) r += Choose(sorted[i], i + 1);
            return r;
        }

        private static int[] Unrank(ulong r, int k)
        {
            var p = new int[k];
            for (int i = k; i >= 1; i--)
            {
                // the largest c with C(c, i) <= r, by doubling then halving (a table holds at most MaxRankClasses)
                int lo = i - 1, hi = i;
                while (hi <= MaxRankClasses && Choose(hi, i) <= r) { lo = hi; hi *= 2; }
                if (hi > MaxRankClasses + 1) hi = MaxRankClasses + 1;
                while (hi - lo > 1) { int mid = (lo + hi) / 2; if (Choose(mid, i) <= r) lo = mid; else hi = mid; }
                int c = lo;
                p[i - 1] = c;
                r -= Choose(c, i);
            }
            return p;
        }

        private static ulong Fnv(byte[] b)
        {
            ulong h = 14695981039346656037UL;
            foreach (byte x in b) { h ^= x; h *= 1099511628211UL; }
            return h;
        }

        private static ulong Zig(int v) => (ulong)(uint)((v << 1) ^ (v >> 31));
        private static int Unzig(ulong u) { uint v = (uint)u; return (int)(v >> 1) ^ -(int)(v & 1); }

        private static void Uint(BinaryWriter w, ulong v)
        {
            while (v >= 0x80) { w.Write((byte)(v | 0x80)); v >>= 7; }
            w.Write((byte)v);
        }

        private static ulong Uint(BinaryReader r)
        {
            ulong v = 0;
            for (int s = 0; s < 64; s += 7)
            {
                byte b = r.ReadByte();
                v |= (ulong)(b & 0x7F) << s;
                if (b < 0x80) return v;
            }
            throw new InvalidDataException("a number runs on too long");
        }

        private static int Count(BinaryReader r)
        {
            ulong v = Uint(r);
            if (v > int.MaxValue) throw new InvalidDataException("a count is not believable");
            return (int)v;
        }

        private static void Str(BinaryWriter w, string s) => w.Write(s ?? "");
    }
}
