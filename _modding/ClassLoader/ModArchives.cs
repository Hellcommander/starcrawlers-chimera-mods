using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Diagnostics;
using System.Threading;

namespace SCClassLoader
{
    /// <summary>
    /// Sound archives: a mod's *.pak files hold its sounds as FLAC, encrypted, in one file, so they ship sealed rather than
    /// as loose audio. Any mod can have one: _modding/audio/sfx_archive.py (or BreachworkModManager.exe --seal-audio) seals a
    /// mod's sounds under a fresh random key and wraps that key with the loader's public key (RSA-2048, OAEP), so only this
    /// loader, which carries the private key, can open it, and no mod author needs anyone's secret. An entry stands in for
    /// the file at the same place in the mod ("sfx/x.wav" in a mod's pak answers for that path), so nothing that names a
    /// sound changes.
    ///
    /// Every entry and the index carry an HMAC-SHA256 tag under the archive key: an archive that was changed or damaged is
    /// refused with a clear log line, never played as noise. The index is encrypted too, so the sound names don't sit in the
    /// clear. The header names the loader key an archive was sealed for, and the loader may carry more than one private key
    /// (loader_private.b64, one base64 line each, the current one first), so after a key rotation older archives still open.
    ///
    /// The private key file is not in the repository: ClassLoader.csproj writes it into this class (PrivateKeys, generated
    /// at build). A loader built without it skips every archive and plays the fallback sounds. Someone who decompiles a
    /// shipped loader can find the private key, so this is sealing against casual copying, not secrecy.
    ///
    /// A background thread reads, checks and decrypts every sound while the game starts, and ModAudio has FMOD decode them
    /// off the main thread into ready samples, so a sound is in memory before anything asks for it and playing it does no
    /// file work and no decoding. A mod with a very large sound set can set "soundsPreload": false in its mod.json: its
    /// archive is then read on demand, one sound at a time, the first time each is played.
    ///
    /// Format BWPK5, little-endian: "BWPK5\0", byte flags (1 index encrypted, 2 tags present), 8 bytes loader-key id
    /// (SHA-256 of the public modulus, first 8), ushort wrappedLength, the wrapped archive key, uint indexLength, the index
    /// (AES-CBC under HMAC(key, "index"), IV first) and its 32-byte tag HMAC(key, "index" || ciphertext), then the entries'
    /// data. Index, decrypted: uint count, per entry ushort nameLength, UTF-8 name, long offset, int size, 16-byte IV,
    /// 32-byte tag HMAC(key, name || IV || ciphertext). Entry key = HMAC(key, lower-case name); AES-256-CBC, PKCS7.
    /// </summary>
    internal static partial class ModArchives
    {
        private const string Magic = "BWPK5\0";
        private const byte FlagIndexEncrypted = 1, FlagTags = 2;

        private class Entry { internal string pak, name; internal long offset; internal int size; internal byte[] iv, tag, key; }

        /// <summary>Full path the sound would have as a loose file -> where it is.</summary>
        private static Dictionary<string, Entry> index;
        /// <summary>Loader-key id (hex) -> private key. Several after a rotation.</summary>
        private static Dictionary<string, RSACryptoServiceProvider> keys;
        /// <summary>Paks whose mod asked not to be preloaded: read on demand.</summary>
        private static readonly HashSet<string> lazyPaks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Sounds read by the background thread, waiting for ModAudio to hand them to FMOD.</summary>
        internal static readonly ConcurrentQueue<(string path, byte[] data)> Ready = new ConcurrentQueue<(string, byte[])>();
        internal static volatile bool PreloadDone;
        internal static long PreloadMs;
        internal static int PreloadThreads;
        /// <summary>Runs from the moment the preload threads start, so the audio pipeline's stages can be timed against one clock.</summary>
        internal static readonly Stopwatch PreloadClock = new Stopwatch();
        private static long readTicks, macTicks, aesTicks;
        /// <summary>Where the preload's time went, summed over its threads (so with several threads it can exceed PreloadMs).</summary>
        internal static string PhaseReport => $"file read {readTicks * 1000 / Stopwatch.Frequency} ms, tag check {macTicks * 1000 / Stopwatch.Frequency} ms, decrypt {aesTicks * 1000 / Stopwatch.Frequency} ms over {PreloadThreads} thread(s), {Crypto.Backend}";

        internal static int Count { get { Load(); return index.Count; } }
        internal static IEnumerable<string> Paths { get { Load(); return index.Keys.ToList(); } }
        /// <summary>False for a loader built without _modding/audio/loader_private.b64.</summary>
        internal static bool HasKey => PrivateKeyB64.Length > 0;
        /// <summary>How many private keys this loader carries (1, or more after a rotation).</summary>
        internal static int KeyCount { get { Load(); return keys?.Count ?? 0; } }

        private static void Load()
        {
            if (index != null) return;
            index = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            if (!HasKey)
            {
                if (ModPacks.Ordered.Any(p => Directory.GetFiles(p.folder, "*.pak", SearchOption.TopDirectoryOnly).Length > 0))
                    Plugin.Log.LogWarning("Sound archives skipped: this loader was built without _modding/audio/loader_private.b64, so the fallback sounds play");
                return;
            }
            keys = new Dictionary<string, RSACryptoServiceProvider>();
            foreach (string line in PrivateKeyB64.Split(new[] { ';', '|', '\n', '\r', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    var rsa = new RSACryptoServiceProvider();
                    rsa.FromXmlString(Encoding.UTF8.GetString(Convert.FromBase64String(line)));
                    byte[] id;
                    using (var sha = SHA256.Create()) id = sha.ComputeHash(rsa.ExportParameters(false).Modulus).Take(8).ToArray();
                    keys[BitConverter.ToString(id).Replace("-", "").ToLowerInvariant()] = rsa;
                }
                catch (Exception e) { Plugin.Log.LogError("A sound-archive private key is unusable: " + e.Message); }
            }
            if (keys.Count == 0) { Plugin.Log.LogError("Sound archives skipped: no usable private key"); return; }
            foreach (ModPack p in ModPacks.Ordered)
            {
                bool lazy = !p.soundsPreload;
                foreach (string pak in Directory.GetFiles(p.folder, "*.pak", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        int added = ReadIndex(pak, p.folder, p.id, index);
                        if (lazy) lazyPaks.Add(pak);
                        if (added > 0 && lazy) Plugin.Log.LogInfo($"{p.id}: {Path.GetFileName(pak)} will be read on demand (soundsPreload false)");
                    }
                    catch (Exception e) { Plugin.Log.LogError($"{p.id}: could not read {Path.GetFileName(pak)}: {e.Message}"); }
                }
            }
            if (index.Count > 0) Plugin.Log.LogInfo($"Sound archives: {index.Count} sounds");
        }

        private static byte[] Hmac(byte[] key, params byte[][] parts) => Crypto.Hmac(key, parts);

        private static bool SameBytes(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        private static byte[] AesCbc(byte[] key, byte[] iv, byte[] data, bool encrypt) => Crypto.AesCbc(key, iv, data, encrypt);

        private static int ReadIndex(string pak, string folder, string modId, Dictionary<string, Entry> into)
        {
            using (var r = new BinaryReader(File.OpenRead(pak)))
            {
                if (Encoding.ASCII.GetString(r.ReadBytes(Magic.Length)) != Magic) throw new InvalidDataException("not a current sound archive; reseal it with sfx_archive.py or the mod manager's --seal-audio");
                byte flags = r.ReadByte();
                string keyId = BitConverter.ToString(r.ReadBytes(8)).Replace("-", "").ToLowerInvariant();
                byte[] wrapped = r.ReadBytes(r.ReadUInt16());
                if (!keys.TryGetValue(keyId, out RSACryptoServiceProvider rsa)) throw new InvalidDataException($"sealed for loader key {keyId}, which this loader does not carry; reseal it with the current public key");
                byte[] key;
                try { key = rsa.Decrypt(wrapped, true); }
                catch (CryptographicException) { throw new InvalidDataException("its wrapped key is damaged; reseal it"); }
                byte[] encIndex = r.ReadBytes(r.ReadInt32());
                byte[] indexTag = r.ReadBytes(32);
                if ((flags & FlagTags) != 0 && !SameBytes(indexTag, Hmac(key, Encoding.ASCII.GetBytes("index"), encIndex)))
                    throw new InvalidDataException("its index tag does not match: the archive was changed or damaged");
                byte[] plain = (flags & FlagIndexEncrypted) != 0
                    ? AesCbc(Hmac(key, Encoding.ASCII.GetBytes("index")), encIndex.Take(16).ToArray(), encIndex.Skip(16).ToArray(), false)
                    : encIndex;
                int added = 0;
                using (var ir = new BinaryReader(new MemoryStream(plain)))
                {
                    int count = ir.ReadInt32();
                    for (int i = 0; i < count; i++)
                    {
                        string name = Encoding.UTF8.GetString(ir.ReadBytes(ir.ReadUInt16()));
                        var e = new Entry { pak = pak, name = name, offset = ir.ReadInt64(), size = ir.ReadInt32(), iv = ir.ReadBytes(16), tag = (flags & FlagTags) != 0 ? ir.ReadBytes(32) : null, key = key };
                        string full = Path.GetFullPath(Path.Combine(folder, name));
                        if (into.TryGetValue(full, out Entry other) && !string.Equals(other.pak, pak, StringComparison.OrdinalIgnoreCase))
                            Plugin.Log.LogWarning($"{modId}: {name} is in both {Path.GetFileName(other.pak)} and {Path.GetFileName(pak)}; the later one wins");
                        into[full] = e;
                        added++;
                    }
                }
                return added;
            }
        }

        /// <summary>Opens one archive file on its own and checks every tag the way playback would (the devtest uses it on a
        /// tampered copy). False with the first problem; nothing is added to the live index.</summary>
        internal static bool Verify(string pak, out string problem)
        {
            Load();
            problem = null;
            if (keys == null || keys.Count == 0) { problem = "no private key"; return false; }
            var own = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            try
            {
                ReadIndex(pak, Path.GetDirectoryName(pak), "verify", own);
                using (var f = File.OpenRead(pak))
                    foreach (Entry e in own.Values.OrderBy(e => e.offset)) ReadFrom(f, e);
                return true;
            }
            catch (Exception x) { problem = x.Message; return false; }
        }

        internal static bool Has(string fullPath)
        {
            Load();
            return index.ContainsKey(fullPath);
        }

        /// <summary>Reads, checks and decrypts every archived sound on a background thread. A loose file wins over the archive,
        /// so those are left out, and so are paks whose mod asked to be read on demand.</summary>
        internal static void StartPreload(bool includeLoose)
        {
            Load();
            var todo = index.Where(kv => !lazyPaks.Contains(kv.Value.pak)).GroupBy(kv => kv.Value.pak).ToList();
            if (todo.Count == 0) { PreloadDone = true; return; }
            // Largest archives first, on a few threads: the work is decryption, which scales with cores.
            var queue = new ConcurrentQueue<IGrouping<string, KeyValuePair<string, Entry>>>(todo.OrderByDescending(g => g.Sum(kv => (long)kv.Value.size)));
            int threads = Math.Min(todo.Count, Math.Max(1, Math.Min(Environment.ProcessorCount - 2, 4)));
            PreloadThreads = threads;
            int running = threads;
            ModAudio.CaptureCore();
            PreloadClock.Start();
            var clock = PreloadClock;
            for (int t = 0; t < threads; t++)
                new Thread(() =>
                {
                    while (queue.TryDequeue(out var pak))
                    {
                        try
                        {
                            using (var f = new FileStream(pak.Key, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 18, FileOptions.SequentialScan))
                                foreach (var kv in pak.OrderBy(k => k.Value.offset))
                                {
                                    if (!includeLoose && File.Exists(kv.Key)) continue;
                                    try { byte[] bytes = ReadFrom(f, kv.Value); if (!ModAudio.Submit(kv.Key, bytes)) Ready.Enqueue((kv.Key, bytes)); }
                                    catch (Exception e) { Plugin.Log.LogError($"Sound archive {Path.GetFileName(pak.Key)}: {kv.Value.name}: {e.Message}"); }
                                }
                        }
                        catch (Exception e) { Plugin.Log.LogError($"Sound archive {Path.GetFileName(pak.Key)}: {e.Message}"); }
                    }
                    if (Interlocked.Decrement(ref running) == 0)
                    {
                        PreloadMs = clock.ElapsedMilliseconds;
                        PreloadDone = true;
                    }
                }) { IsBackground = true, Name = "Breachwork sound preload " + t }.Start();
        }

        /// <summary>A sound's bytes right now, on the calling thread: for one asked for before the preload reached it, or from a pak read on demand.</summary>
        internal static byte[] Read(string fullPath)
        {
            Load();
            if (!index.TryGetValue(fullPath, out Entry e)) return null;
            try
            {
                using (var f = File.OpenRead(e.pak)) return ReadFrom(f, e);
            }
            catch (Exception x)
            {
                Plugin.Log.LogError($"Sound archive {Path.GetFileName(e.pak)}: could not read {e.name}: {x.Message}");
                return null;
            }
        }

        private static byte[] ReadFrom(FileStream f, Entry e)
        {
            var data = new byte[e.size];
            long t0 = Stopwatch.GetTimestamp();
            f.Seek(e.offset, SeekOrigin.Begin);
            for (int got = 0, n; got < data.Length; got += n)
                if ((n = f.Read(data, got, data.Length - got)) <= 0) throw new EndOfStreamException();
            long t1 = Stopwatch.GetTimestamp();
            if (e.tag != null && !SameBytes(e.tag, Hmac(e.key, Encoding.UTF8.GetBytes(e.name), e.iv, data)))
                throw new InvalidDataException("tag does not match: changed or damaged, not played");
            long t2 = Stopwatch.GetTimestamp();
            byte[] plain = AesCbc(Hmac(e.key, Encoding.UTF8.GetBytes(e.name.Replace('\\', '/').ToLowerInvariant())), e.iv, data, false);
            long t3 = Stopwatch.GetTimestamp();
            Interlocked.Add(ref readTicks, t1 - t0); Interlocked.Add(ref macTicks, t2 - t1); Interlocked.Add(ref aesTicks, t3 - t2);
            return plain;
        }
    }
}
