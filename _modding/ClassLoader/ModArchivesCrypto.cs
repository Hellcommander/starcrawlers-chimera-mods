using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace SCClassLoader
{
    /// <summary>
    /// The archive's AES-256-CBC and HMAC-SHA256, through Windows CNG (bcrypt.dll) when it is there and agrees with the managed
    /// code on a self-test, otherwise through Mono's managed classes. Mono's managed AES runs at some 30 MB/s; CNG uses the
    /// processor's AES instructions and runs at gigabytes a second, which is what makes the start-up preload short.
    /// The sounds come out bit for bit the same either way: this is the same cipher, only faster.
    /// </summary>
    internal static partial class ModArchives
    {
        private static class Crypto
        {
            private const uint BCRYPT_ALG_HANDLE_HMAC_FLAG = 8, BCRYPT_BLOCK_PADDING = 1;
            private static readonly IntPtr hAes, hHmac;
            private static readonly bool cng;

            internal static string Backend => cng ? "hardware AES via Windows CNG" : "managed crypto";

            [DllImport("bcrypt.dll")] private static extern int BCryptOpenAlgorithmProvider(out IntPtr phAlgorithm, [MarshalAs(UnmanagedType.LPWStr)] string pszAlgId, [MarshalAs(UnmanagedType.LPWStr)] string pszImplementation, uint dwFlags);
            [DllImport("bcrypt.dll")] private static extern int BCryptSetProperty(IntPtr hObject, [MarshalAs(UnmanagedType.LPWStr)] string pszProperty, byte[] pbInput, int cbInput, uint dwFlags);
            [DllImport("bcrypt.dll")] private static extern int BCryptGenerateSymmetricKey(IntPtr hAlgorithm, out IntPtr phKey, IntPtr pbKeyObject, int cbKeyObject, byte[] pbSecret, int cbSecret, uint dwFlags);
            [DllImport("bcrypt.dll")] private static extern int BCryptDecrypt(IntPtr hKey, byte[] pbInput, int cbInput, IntPtr pPaddingInfo, byte[] pbIV, int cbIV, byte[] pbOutput, int cbOutput, out int pcbResult, uint dwFlags);
            [DllImport("bcrypt.dll")] private static extern int BCryptDestroyKey(IntPtr hKey);
            [DllImport("bcrypt.dll")] private static extern int BCryptCreateHash(IntPtr hAlgorithm, out IntPtr phHash, IntPtr pbHashObject, int cbHashObject, byte[] pbSecret, int cbSecret, uint dwFlags);
            [DllImport("bcrypt.dll")] private static extern int BCryptHashData(IntPtr hHash, byte[] pbInput, int cbInput, uint dwFlags);
            [DllImport("bcrypt.dll")] private static extern int BCryptFinishHash(IntPtr hHash, byte[] pbOutput, int cbOutput, uint dwFlags);
            [DllImport("bcrypt.dll")] private static extern int BCryptDestroyHash(IntPtr hHash);

            static Crypto()
            {
                try
                {
                    if (BCryptOpenAlgorithmProvider(out hAes, "AES", null, 0) != 0) return;
                    byte[] cbc = System.Text.Encoding.Unicode.GetBytes("ChainingModeCBC\0");
                    if (BCryptSetProperty(hAes, "ChainingMode", cbc, cbc.Length, 0) != 0) return;
                    if (BCryptOpenAlgorithmProvider(out hHmac, "SHA256", null, BCRYPT_ALG_HANDLE_HMAC_FLAG) != 0) return;
                    cng = SelfTest();
                }
                catch (Exception) { cng = false; }   // no bcrypt.dll (not Windows), or a call that is not there
            }

            /// <summary>CNG must give the same bytes as the managed code on random data, or it is not used.</summary>
            private static bool SelfTest()
            {
                var rng = new Random(7);
                for (int round = 0; round < 3; round++)
                {
                    var key = new byte[32]; var iv = new byte[16]; var plain = new byte[16 * (round + 1) + round * 5];
                    rng.NextBytes(key); rng.NextBytes(iv); rng.NextBytes(plain);
                    byte[] enc = ManagedAes(key, iv, plain, true);
                    if (!CngDecrypt(key, iv, enc).SequenceEqual(plain)) return false;
                    if (!CngHmac(key, new[] { plain, iv }).SequenceEqual(ManagedHmac(key, new[] { plain, iv }))) return false;
                }
                return true;
            }

            internal static byte[] Hmac(byte[] key, byte[][] parts) => cng ? CngHmac(key, parts) : ManagedHmac(key, parts);

            internal static byte[] AesCbc(byte[] key, byte[] iv, byte[] data, bool encrypt) => cng && !encrypt ? CngDecrypt(key, iv, data) : ManagedAes(key, iv, data, encrypt);

            private static byte[] ManagedHmac(byte[] key, byte[][] parts)
            {
                using (var h = new HMACSHA256(key)) return h.ComputeHash(parts.SelectMany(b => b).ToArray());
            }

            private static byte[] ManagedAes(byte[] key, byte[] iv, byte[] data, bool encrypt)
            {
                using (var aes = Aes.Create())
                {
                    aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7; aes.Key = key; aes.IV = iv;
                    using (ICryptoTransform t = encrypt ? aes.CreateEncryptor() : aes.CreateDecryptor()) return t.TransformFinalBlock(data, 0, data.Length);
                }
            }

            private static byte[] CngHmac(byte[] key, byte[][] parts)
            {
                if (BCryptCreateHash(hHmac, out IntPtr h, IntPtr.Zero, 0, key, key.Length, 0) != 0) throw new CryptographicException("CNG HMAC create failed");
                try
                {
                    foreach (byte[] part in parts)
                        if (BCryptHashData(h, part, part.Length, 0) != 0) throw new CryptographicException("CNG HMAC data failed");
                    var tag = new byte[32];
                    if (BCryptFinishHash(h, tag, tag.Length, 0) != 0) throw new CryptographicException("CNG HMAC finish failed");
                    return tag;
                }
                finally { BCryptDestroyHash(h); }
            }

            private static byte[] CngDecrypt(byte[] key, byte[] iv, byte[] data)
            {
                if (BCryptGenerateSymmetricKey(hAes, out IntPtr k, IntPtr.Zero, 0, key, key.Length, 0) != 0) throw new CryptographicException("CNG AES key failed");
                try
                {
                    var ivCopy = (byte[])iv.Clone();                       // CNG advances the IV in place
                    var outBuf = new byte[data.Length];
                    int status = BCryptDecrypt(k, data, data.Length, IntPtr.Zero, ivCopy, ivCopy.Length, outBuf, outBuf.Length, out int got, BCRYPT_BLOCK_PADDING);
                    if (status != 0) throw new CryptographicException($"CNG AES decrypt failed (0x{status:X8}): bad padding, changed or damaged");
                    if (got != outBuf.Length) Array.Resize(ref outBuf, got);
                    return outBuf;
                }
                finally { BCryptDestroyKey(k); }
            }
        }
    }
}
