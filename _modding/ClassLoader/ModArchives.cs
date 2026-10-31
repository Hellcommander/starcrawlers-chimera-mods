using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace SCClassLoader
{
    /// <summary>
    /// Sound archives: a mod's *.pak files hold its sounds as FLAC, encrypted, in one file, so they ship sealed rather than
    /// as loose audio. Made by _modding/audio/sfx_archive.py. An entry stands in for the file at the same place in the mod
    /// ("sfx/x.wav" in a mod's pak answers for that path), so nothing that names a sound changes.
    ///
    /// The key is _modding/audio/archive.key, which is not in the repository: ClassLoader.csproj writes it into this class
    /// (the KeyHex constant, generated at build) so the dll and the paks it ships with come from the same key. A loader built
    /// without the file has an empty KeyHex, skips every archive and plays the fallback sounds. Someone who decompiles a
    /// shipped loader can find the key, so this is sealing against casual copying, not secrecy.
    ///
    /// A background thread reads and decrypts every sound while the game starts, and ModAudio has FMOD decode them off the
    /// main thread into ready samples, so a sound is in memory before anything asks for it and playing it does no file
    /// work and no decoding: faster than streaming it on demand ever could be.
    ///
    /// Format, little-endian: "BWPK3\0", 8 bytes key id (SHA-256 of the key, first 8), int count, then per entry ushort
    /// nameLength, UTF-8 name, long offset, int size, 16-byte IV; offsets are from the start of the file. Each entry is its
    /// stored bytes (FLAC, or the original file if that is smaller) under AES-256-CBC with PKCS7 padding, keyed with
    /// HMAC-SHA256(key, lower-case name).
    /// </summary>
    internal static partial class ModArchives
    {
        private const string Magic = "BWPK3\0";

        private class Entry { internal string pak, name; internal long offset; internal int size; internal byte[] iv; }

        /// <summary>Full path the sound would have as a loose file -> where it is.</summary>
        private static Dictionary<string, Entry> index;
        private static byte[] key, keyId;

        /// <summary>Sounds read by the background thread, waiting for ModAudio to hand them to FMOD.</summary>
        internal static readonly ConcurrentQueue<(string path, byte[] data)> Ready = new ConcurrentQueue<(string, byte[])>();
        internal static volatile bool PreloadDone;
        internal static long PreloadMs;

        internal static int Count { get { Load(); return index.Count; } }
        internal static IEnumerable<string> Paths { get { Load(); return index.Keys.ToList(); } }
        /// <summary>False for a loader built without _modding/audio/archive.key.</summary>
        internal static bool HasKey => KeyHex.Length == 64;

        private static void Load()
        {
            if (index != null) return;
            index = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            if (!HasKey)
            {
                if (ModPacks.Ordered.Any(p => Directory.GetFiles(p.folder, "*.pak", SearchOption.TopDirectoryOnly).Length > 0))
                    Plugin.Log.LogWarning("Sound archives skipped: this loader was built without _modding/audio/archive.key, so the fallback sounds play");
                return;
            }
            key = Enumerable.Range(0, 32).Select(i => Convert.ToByte(KeyHex.Substring(i * 2, 2), 16)).ToArray();
            using (var sha = SHA256.Create()) keyId = sha.ComputeHash(key).Take(8).ToArray();
            foreach (ModPack p in ModPacks.Ordered)
                foreach (string pak in Directory.GetFiles(p.folder, "*.pak", SearchOption.TopDirectoryOnly))
                {
                    try { ReadIndex(pak, p.folder); }
                    catch (Exception e) { Plugin.Log.LogError($"{p.id}: could not read {Path.GetFileName(pak)}: {e.Message}"); }
                }
            if (index.Count > 0) Plugin.Log.LogInfo($"Sound archives: {index.Count} sounds");
        }

        private static void ReadIndex(string pak, string folder)
        {
            using (var r = new BinaryReader(File.OpenRead(pak)))
            {
                if (Encoding.ASCII.GetString(r.ReadBytes(Magic.Length)) != Magic) throw new InvalidDataException("not a current sound archive; rebuild it with sfx_archive.py");
                if (!r.ReadBytes(8).SequenceEqual(keyId)) throw new InvalidDataException("sealed with a different key than this loader was built with; rebuild both from the same archive.key");
                int count = r.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    string name = Encoding.UTF8.GetString(r.ReadBytes(r.ReadUInt16()));
                    var e = new Entry { pak = pak, name = name, offset = r.ReadInt64(), size = r.ReadInt32(), iv = r.ReadBytes(16) };
                    index[Path.GetFullPath(Path.Combine(folder, name))] = e;
                }
            }
        }

        internal static bool Has(string fullPath)
        {
            Load();
            return index.ContainsKey(fullPath);
        }

        /// <summary>Reads and decrypts every archived sound on a background thread. A loose file wins over the archive, so those are left out.</summary>
        internal static void StartPreload(bool includeLoose)
        {
            Load();
            var todo = index.GroupBy(kv => kv.Value.pak).ToList();
            if (todo.Count == 0) { PreloadDone = true; return; }
            new Thread(() =>
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                foreach (var pak in todo)
                {
                    try
                    {
                        using (var f = new FileStream(pak.Key, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 18, FileOptions.SequentialScan))
                            foreach (var kv in pak.OrderBy(k => k.Value.offset))
                                if (includeLoose || !File.Exists(kv.Key)) Ready.Enqueue((kv.Key, ReadFrom(f, kv.Value)));
                    }
                    catch (Exception e) { Plugin.Log.LogError($"Sound archive {Path.GetFileName(pak.Key)}: {e.Message}"); }
                }
                PreloadMs = clock.ElapsedMilliseconds;
                PreloadDone = true;
            }) { IsBackground = true, Name = "Breachwork sound preload" }.Start();
        }

        /// <summary>A sound's bytes right now, on the calling thread: for one asked for before the preload reached it.</summary>
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
            f.Seek(e.offset, SeekOrigin.Begin);
            for (int got = 0, n; got < data.Length; got += n)
                if ((n = f.Read(data, got, data.Length - got)) <= 0) throw new EndOfStreamException();
            return Unseal(data, e);
        }

        private static byte[] Unseal(byte[] data, Entry e)
        {
            byte[] entryKey;
            using (var h = new HMACSHA256(key)) entryKey = h.ComputeHash(Encoding.UTF8.GetBytes(e.name.Replace('\\', '/').ToLowerInvariant()));
            using (var aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = entryKey;
                aes.IV = e.iv;
                using (ICryptoTransform d = aes.CreateDecryptor()) return d.TransformFinalBlock(data, 0, data.Length);
            }
        }
    }
}
