using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ChimeraModManager
{
    /// <summary>
    /// BreachworkModManager.exe --seal-audio &lt;mod folder&gt; [--remove-loose]: embedded audio for any mod, without Python.
    /// Every .wav/.ogg/.mp3 in the mod (outside plugins/bin/obj) goes into the mod's sounds.pak under a fresh random key,
    /// which is wrapped with the Breachwork Loader's public key (SealPublicKey.cs), so only the loader can open it and
    /// the mod author needs no secret. Every entry and the index carry an HMAC tag, and the index is encrypted. The format
    /// is the loader's BWPK5 (see ModArchives.cs in the loader); the files are stored as they are (no FLAC here), so a
    /// .wav-heavy mod's pak is about the size of its sounds.
    /// </summary>
    internal static class SealAudio
    {
        private static readonly string[] Kinds = { ".wav", ".ogg", ".mp3" };

        internal static int Run(string modFolder, bool removeLoose, Action<string> print)
        {
            modFolder = Path.GetFullPath(modFolder);
            if (!File.Exists(Path.Combine(modFolder, "mod.json"))) { print($"{modFolder} has no mod.json; give the mod's folder."); return 2; }
            var files = Directory.GetFiles(modFolder, "*", SearchOption.AllDirectories)
                .Where(f => Kinds.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .Where(f => !Path.GetDirectoryName(f).Substring(modFolder.Length).Split(Path.DirectorySeparatorChar).Any(part => part == "plugins" || part == "bin" || part == "obj"))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
            if (files.Count == 0) { print("No sounds found in the mod."); return 1; }

            var key = new byte[32];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(key);
            byte[] wrapped, loaderId;
            using (var rsa = new RSACryptoServiceProvider())
            {
                rsa.FromXmlString(SealPublicKey.Xml);
                wrapped = rsa.Encrypt(key, true);
                using (var sha = SHA256.Create()) loaderId = sha.ComputeHash(rsa.ExportParameters(false).Modulus).Take(8).ToArray();
            }
            var blobs = new List<(byte[] name, byte[] iv, byte[] data, byte[] tag)>();
            long raw = 0;
            foreach (var f in files)
            {
                string name = f.Substring(modFolder.Length + 1).Replace('\\', '/');
                byte[] plain = File.ReadAllBytes(f);
                raw += plain.Length;
                byte[] iv = Seal(key, name, plain, out byte[] enc);
                blobs.Add((Encoding.UTF8.GetBytes(name), iv, enc, Hmac(key, Encoding.UTF8.GetBytes(name), iv, enc)));
            }
            int plainIndexLen = 4 + blobs.Sum(b => 2 + b.name.Length + 8 + 4 + 16 + 32);
            int encIndexLen = 16 + (plainIndexLen / 16 + 1) * 16;
            long offset = 6 + 1 + 8 + 2 + wrapped.Length + 4 + encIndexLen + 32;
            byte[] plainIndex;
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(blobs.Count);
                foreach (var b in blobs)
                {
                    w.Write((ushort)b.name.Length); w.Write(b.name); w.Write(offset); w.Write(b.data.Length); w.Write(b.iv); w.Write(b.tag);
                    offset += b.data.Length;
                }
                plainIndex = ms.ToArray();
            }
            byte[] indexKey = Hmac(key, Encoding.ASCII.GetBytes("index"));
            byte[] encIndex;
            using (var aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7; aes.Key = indexKey; aes.GenerateIV();
                using (var e = aes.CreateEncryptor()) encIndex = aes.IV.Concat(e.TransformFinalBlock(plainIndex, 0, plainIndex.Length)).ToArray();
            }
            if (encIndex.Length != encIndexLen) throw new InvalidOperationException("index size mismatch");
            byte[] indexTag = Hmac(key, Encoding.ASCII.GetBytes("index"), encIndex);
            string pak = Path.Combine(modFolder, "sounds.pak");
            using (var w = new BinaryWriter(File.Create(pak)))
            {
                w.Write(Encoding.ASCII.GetBytes("BWPK5\0"));
                w.Write((byte)3);
                w.Write(loaderId);
                w.Write((ushort)wrapped.Length); w.Write(wrapped);
                w.Write(encIndex.Length); w.Write(encIndex); w.Write(indexTag);
                foreach (var b in blobs) w.Write(b.data);
            }
            print($"{Path.GetFileName(modFolder)}: {files.Count} sounds, {raw / 1048576.0:F1} MB -> sounds.pak ({offset / 1048576.0:F1} MB), sealed for the Breachwork Loader (key {BitConverter.ToString(loaderId).Replace("-", "").ToLowerInvariant()}), tagged against tampering.");
            if (removeLoose)
            {
                foreach (var f in files) File.Delete(f);
                print("Removed the loose sound files; the mod plays from sounds.pak.");
            }
            else print("The loose files are still there and still win over the pak; remove them from the copy you share.");
            return 0;
        }

        private static byte[] Hmac(byte[] key, params byte[][] parts)
        {
            using (var h = new HMACSHA256(key)) return h.ComputeHash(parts.SelectMany(b => b).ToArray());
        }

        private static byte[] Seal(byte[] key, string name, byte[] plain, out byte[] enc)
        {
            byte[] entryKey = Hmac(key, Encoding.UTF8.GetBytes(name.ToLowerInvariant()));
            using (var aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7; aes.Key = entryKey;
                aes.GenerateIV();
                using (var e = aes.CreateEncryptor()) enc = e.TransformFinalBlock(plain, 0, plain.Length);
                return aes.IV;
            }
        }
    }
}
