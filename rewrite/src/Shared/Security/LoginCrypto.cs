using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace EQClassic.Shared.Security
{
    /// <summary>
    /// Cryptography of the login exchange, limited to primitives available on netstandard2.1 and
    /// Unity's Mono runtime (no AesGcm, no X25519): RSA-OAEP(SHA-1) to send credentials and a client
    /// secret, then AES-256-CBC + HMAC-SHA256 (encrypt-then-MAC) keyed from that secret.
    /// </summary>
    public static class LoginCrypto
    {
        public const int NonceSize = 32;
        public const int SecretSize = 32;
        private const int IvSize = 16;
        private const int MacSize = 32;

        public static byte[] RandomBytes(int count)
        {
            var bytes = new byte[count];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(bytes);
            return bytes;
        }

        /// <summary>SHA-256 of modulus and exponent, hex: what a client pins to detect a man in the middle.</summary>
        public static string Fingerprint(byte[] modulus, byte[] exponent)
        {
            using (var sha = SHA256.Create())
            {
                var data = new byte[modulus.Length + exponent.Length];
                Buffer.BlockCopy(modulus, 0, data, 0, modulus.Length);
                Buffer.BlockCopy(exponent, 0, data, modulus.Length, exponent.Length);
                return ToHex(sha.ComputeHash(data));
            }
        }

        /// <summary>Client side: nonce | secret | username | password, RSA-OAEP encrypted for the server.</summary>
        public static byte[] SealCredentials(byte[] modulus, byte[] exponent, byte[] nonce, byte[] secret, string username, string password)
        {
            byte[] plain;
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms, Encoding.UTF8))
            {
                w.Write(nonce);
                w.Write(secret);
                w.Write(username);
                w.Write(password);
                w.Flush();
                plain = ms.ToArray();
            }
            using (var rsa = RSA.Create())
            {
                rsa.ImportParameters(new RSAParameters { Modulus = modulus, Exponent = exponent });
                return rsa.Encrypt(plain, RSAEncryptionPadding.OaepSHA1);
            }
        }

        /// <summary>Server side. Returns false for anything that does not decrypt to the expected layout.</summary>
        public static bool TryOpenCredentials(RSA serverKey, byte[] sealedCredentials, out byte[] nonce, out byte[] secret, out string username, out string password)
        {
            nonce = secret = Array.Empty<byte>();
            username = password = "";
            try
            {
                var plain = serverKey.Decrypt(sealedCredentials, RSAEncryptionPadding.OaepSHA1);
                using (var r = new BinaryReader(new MemoryStream(plain), Encoding.UTF8))
                {
                    nonce = r.ReadBytes(NonceSize);
                    secret = r.ReadBytes(SecretSize);
                    username = r.ReadString();
                    password = r.ReadString();
                    return nonce.Length == NonceSize && secret.Length == SecretSize && r.BaseStream.Position == plain.Length;
                }
            }
            catch (Exception e) when (e is CryptographicException || e is EndOfStreamException || e is IOException || e is FormatException)
            {
                return false;
            }
        }

        /// <summary>Encryption and MAC keys of a login session, derived from the client secret and the server nonce.</summary>
        public static SessionKeys DeriveSessionKeys(byte[] secret, byte[] nonce) =>
            new SessionKeys(Derive(secret, "EQClassic enc", nonce), Derive(secret, "EQClassic mac", nonce));

        /// <summary>iv | AES-256-CBC(plain) | HMAC-SHA256(iv | ciphertext).</summary>
        public static byte[] Seal(SessionKeys keys, byte[] plain)
        {
            var iv = RandomBytes(IvSize);
            byte[] cipher;
            using (var aes = CreateAes(keys.Encryption))
            using (var enc = aes.CreateEncryptor(keys.Encryption, iv))
                cipher = enc.TransformFinalBlock(plain, 0, plain.Length);
            var box = new byte[IvSize + cipher.Length + MacSize];
            Buffer.BlockCopy(iv, 0, box, 0, IvSize);
            Buffer.BlockCopy(cipher, 0, box, IvSize, cipher.Length);
            var mac = Mac(keys.Authentication, box, IvSize + cipher.Length);
            Buffer.BlockCopy(mac, 0, box, IvSize + cipher.Length, MacSize);
            return box;
        }

        /// <summary>Checks the MAC before decrypting; returns false on any tampering.</summary>
        public static bool TryOpen(SessionKeys keys, byte[] box, out byte[] plain)
        {
            plain = Array.Empty<byte>();
            if (box.Length < IvSize + 16 + MacSize)
                return false;
            int signed = box.Length - MacSize;
            var expected = Mac(keys.Authentication, box, signed);
            if (!FixedTimeEquals(expected, box, signed))
                return false;
            var iv = new byte[IvSize];
            Buffer.BlockCopy(box, 0, iv, 0, IvSize);
            try
            {
                using (var aes = CreateAes(keys.Encryption))
                using (var dec = aes.CreateDecryptor(keys.Encryption, iv))
                    plain = dec.TransformFinalBlock(box, IvSize, signed - IvSize);
                return true;
            }
            catch (CryptographicException)
            {
                return false;
            }
        }

        private static Aes CreateAes(byte[] key)
        {
            var aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = key;
            return aes;
        }

        private static byte[] Derive(byte[] secret, string label, byte[] nonce)
        {
            using (var hmac = new HMACSHA256(secret))
            {
                var labelBytes = Encoding.ASCII.GetBytes(label);
                var data = new byte[labelBytes.Length + nonce.Length];
                Buffer.BlockCopy(labelBytes, 0, data, 0, labelBytes.Length);
                Buffer.BlockCopy(nonce, 0, data, labelBytes.Length, nonce.Length);
                return hmac.ComputeHash(data);
            }
        }

        private static byte[] Mac(byte[] key, byte[] data, int count)
        {
            using (var hmac = new HMACSHA256(key))
                return hmac.ComputeHash(data, 0, count);
        }

        private static bool FixedTimeEquals(byte[] expected, byte[] box, int offset)
        {
            int diff = 0;
            for (int i = 0; i < expected.Length; i++)
                diff |= expected[i] ^ box[offset + i];
            return diff == 0;
        }

        public static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
                return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++)
                diff |= a[i] ^ b[i];
            return diff == 0;
        }

        private static string ToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }

    public sealed class SessionKeys
    {
        public SessionKeys(byte[] encryption, byte[] authentication)
        {
            Encryption = encryption;
            Authentication = authentication;
        }

        public byte[] Encryption { get; }
        public byte[] Authentication { get; }
    }
}
