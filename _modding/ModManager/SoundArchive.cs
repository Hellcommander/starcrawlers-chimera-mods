using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ChimeraModManager
{
    /// <summary>
    /// Owner tool, never in the player build: opens a mod's sealed sound archive (BWPK5, see the loader's ModArchives.cs)
    /// with the loader's PRIVATE key, so the master copy's sounds can be listed, played, exported, replaced and resealed.
    ///
    /// The private key is never compiled into the manager. It is read at run time from the owner's own key file,
    /// _modding/audio/loader_private.b64 in the game folder (gitignored, never shipped), or from a key file the owner
    /// picks; a copy without it can still seal (public key) but not open. One base64 RSA key per line, the current one
    /// first, the same file the loader build bakes in, so archives sealed for an older key still open after a rotation.
    /// </summary>
    internal sealed class SoundArchive
    {
        internal const string KeyFileName = "loader_private.b64";

        internal sealed class Item
        {
            internal string Name;          // archive path, '/' separated ("sfx/hit.wav")
            internal byte[] Data;          // the sound's bytes as stored (PCM wav, FLAC, ogg or mp3)
            internal bool Changed;
            internal string From;          // the file it was replaced or added from, when changed

            internal string Kind
            {
                get
                {
                    if (Data == null || Data.Length < 4) return "?";
                    string head = Encoding.ASCII.GetString(Data, 0, 4);
                    return head == "RIFF" ? "WAV" : head == "fLaC" ? "FLAC" : head == "OggS" ? "OGG"
                        : head.StartsWith("ID3") || (Data[0] == 0xFF && (Data[1] & 0xE0) == 0xE0) ? "MP3" : "?";
                }
            }
        }

        internal readonly string Pak;
        internal readonly string KeyId;
        internal readonly List<Item> Items = new List<Item>();
        internal bool Dirty => removed || Items.Any(i => i.Changed);
        private bool removed;

        private SoundArchive(string pak, string keyId) { Pak = pak; KeyId = keyId; }

        /// <summary>The owner's private keys: from `keyFile` if given, else the game folder's _modding/audio key file.
        /// Empty when there is none (a player's copy).</summary>
        internal static List<RSACryptoServiceProvider> OwnerKeys(string gameDir, string keyFile, out string source)
        {
            source = keyFile ?? Path.Combine(gameDir, "_modding", "audio", KeyFileName);
            var keys = new List<RSACryptoServiceProvider>();
            if (!File.Exists(source)) return keys;
            foreach (string line in File.ReadAllText(source).Split(new[] { ';', '|', '\n', '\r', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    var rsa = new RSACryptoServiceProvider();
                    rsa.FromXmlString(Encoding.UTF8.GetString(Convert.FromBase64String(line)));
                    if (!rsa.PublicOnly) keys.Add(rsa);
                }
                catch (FormatException) { }
                catch (CryptographicException) { }
            }
            return keys;
        }

        private static string IdOf(RSACryptoServiceProvider rsa)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(rsa.ExportParameters(false).Modulus).Take(8).ToArray()).Replace("-", "").ToLowerInvariant();
        }

        private static bool Same(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int d = 0;
            for (int i = 0; i < a.Length; i++) d |= a[i] ^ b[i];
            return d == 0;
        }

        private static byte[] AesDecrypt(byte[] key, byte[] iv, byte[] data)
        {
            using (var aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7; aes.Key = key; aes.IV = iv;
                using (var d = aes.CreateDecryptor()) return d.TransformFinalBlock(data, 0, data.Length);
            }
        }

        /// <summary>Opens and fully checks an archive the way the loader does (index tag, every entry's tag), decrypting
        /// every sound into memory. Throws with a plain reason when it cannot.</summary>
        internal static SoundArchive Open(string pak, IList<RSACryptoServiceProvider> keys)
        {
            using (var r = new BinaryReader(File.OpenRead(pak)))
            {
                if (Encoding.ASCII.GetString(r.ReadBytes(6)) != "BWPK5\0") throw new InvalidDataException("not a BWPK5 sound archive");
                byte flags = r.ReadByte();
                string keyId = BitConverter.ToString(r.ReadBytes(8)).Replace("-", "").ToLowerInvariant();
                byte[] wrapped = r.ReadBytes(r.ReadUInt16());
                var rsa = keys.FirstOrDefault(k => IdOf(k) == keyId)
                          ?? throw new InvalidDataException($"sealed for loader key {keyId}, which your key file does not hold");
                byte[] key;
                try { key = rsa.Decrypt(wrapped, true); }
                catch (CryptographicException) { throw new InvalidDataException("its wrapped key is damaged"); }
                byte[] encIndex = r.ReadBytes(r.ReadInt32());
                byte[] indexTag = r.ReadBytes(32);
                byte[] indexKey = SealAudio.Hmac(key, Encoding.ASCII.GetBytes("index"));
                if ((flags & 2) != 0 && !Same(indexTag, SealAudio.Hmac(key, Encoding.ASCII.GetBytes("index"), encIndex)))
                    throw new InvalidDataException("its index tag does not match: the archive was changed or damaged");
                byte[] plain = (flags & 1) != 0 ? AesDecrypt(indexKey, encIndex.Take(16).ToArray(), encIndex.Skip(16).ToArray()) : encIndex;
                var arc = new SoundArchive(pak, keyId);
                var rows = new List<(string name, long offset, int size, byte[] iv, byte[] tag)>();
                using (var ir = new BinaryReader(new MemoryStream(plain)))
                {
                    int count = ir.ReadInt32();
                    for (int i = 0; i < count; i++)
                        rows.Add((Encoding.UTF8.GetString(ir.ReadBytes(ir.ReadUInt16())), ir.ReadInt64(), ir.ReadInt32(), ir.ReadBytes(16), (flags & 2) != 0 ? ir.ReadBytes(32) : null));
                }
                foreach (var e in rows.OrderBy(e => e.offset))
                {
                    r.BaseStream.Seek(e.offset, SeekOrigin.Begin);
                    byte[] data = r.ReadBytes(e.size);
                    if (data.Length != e.size) throw new EndOfStreamException($"{e.name} is cut short");
                    if (e.tag != null && !Same(e.tag, SealAudio.Hmac(key, Encoding.UTF8.GetBytes(e.name), e.iv, data)))
                        throw new InvalidDataException($"{e.name}: tag does not match (changed or damaged)");
                    byte[] sound = AesDecrypt(SealAudio.Hmac(key, Encoding.UTF8.GetBytes(e.name.Replace('\\', '/').ToLowerInvariant())), e.iv, data);
                    arc.Items.Add(new Item { Name = e.name, Data = sound });
                }
                arc.Items.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                return arc;
            }
        }

        internal void Replace(Item item, string file)
        {
            item.Data = File.ReadAllBytes(file); item.Changed = true; item.From = file;
        }

        internal Item Add(string name, string file)
        {
            name = name.Replace('\\', '/').TrimStart('/');
            if (Items.Any(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException($"{name} is already in the archive; replace it instead");
            var it = new Item { Name = name, Data = File.ReadAllBytes(file), Changed = true, From = file };
            Items.Add(it);
            Items.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return it;
        }

        internal void Remove(Item item) { if (Items.Remove(item)) removed = true; }

        /// <summary>Reseals every item with the current loader public key: written beside the archive first, opened and
        /// checked with the owner's keys, and only then put in place; the old archive is moved into `backupDir` (outside the
        /// mod, so a release never packs it), time-stamped. Returns the loader key id; `backup` is where the old one went.</summary>
        internal string Save(IList<RSACryptoServiceProvider> keys, string backupDir, out string backup)
        {
            backup = null;
            string tmp = Pak + ".new";
            SealAudio.WritePak(tmp, Items.Select(i => (i.Name, i.Data)).ToList(), out string loaderHex);
            var check = Open(tmp, keys);   // throws if the new archive does not read back
            if (check.Items.Count != Items.Count || check.Items.Zip(Items, (a, b) => a.Name == b.Name && a.Data.SequenceEqual(b.Data)).Any(ok => !ok))
            {
                File.Delete(tmp);
                throw new InvalidDataException("the resealed archive did not read back the same; nothing was replaced");
            }
            if (File.Exists(Pak))
            {
                Directory.CreateDirectory(backupDir);
                backup = Path.Combine(backupDir, Path.GetFileNameWithoutExtension(Pak) + DateTime.Now.ToString("-yyyyMMdd-HHmmss") + ".pak");
                File.Move(Pak, backup);
            }
            File.Move(tmp, Pak);
            foreach (var i in Items) { i.Changed = false; i.From = null; }
            removed = false;
            return loaderHex;
        }
    }
}
