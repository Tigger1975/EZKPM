using System;
using System.Security.Cryptography;
using System.Text;
using System.Linq;

namespace EZKPM.Client.Core.Cryptography
{
    public static class SecretKeyService
    {
        private const string Base32Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        /// <summary>
        /// Generiert einen neuen 128-Bit (16 Byte) Secret Key und gibt ihn im 34-stelligen Format zurück:
        /// EZ-XXXXX-XXXXX-XXXXX-XXXXX-XXXXXX (26 Base32 Chars + Prefix + Dashes = 33/34 Chars)
        /// </summary>
        public static string GenerateSecretKey()
        {
            byte[] secretBytes = new byte[16];
            RandomNumberGenerator.Fill(secretBytes);
            string base32 = ToBase32(secretBytes);

            // Format: EZ-XXXXX-XXXXX-XXXXX-XXXXX-XXXXXX
            // base32 is exactly 26 characters for 16 bytes.
            return $"EZ-{base32.Substring(0, 5)}-{base32.Substring(5, 5)}-{base32.Substring(10, 5)}-{base32.Substring(15, 5)}-{base32.Substring(20, 6)}";
        }

        /// <summary>
        /// Parst einen im UI eingegebenen Secret Key zurück in die 16-Byte Rohdaten.
        /// Ignoriert Bindestriche und Prefix.
        /// </summary>
        public static byte[] ParseSecretKey(string formattedKey)
        {
            if (string.IsNullOrWhiteSpace(formattedKey))
                throw new ArgumentException("Secret Key darf nicht leer sein.");

            string cleanKey = formattedKey.ToUpper().Replace("-", "").Replace(" ", "");
            if (cleanKey.StartsWith("EZ"))
            {
                cleanKey = cleanKey.Substring(2);
            }

            if (cleanKey.Length != 26)
            {
                throw new ArgumentException("Ungültiges Secret Key Format.");
            }

            return FromBase32(cleanKey);
        }

        private static string ToBase32(byte[] bytes)
        {
            StringBuilder sb = new StringBuilder();
            int buffer = 0;
            int bitsLeft = 0;
            foreach (byte b in bytes)
            {
                buffer <<= 8;
                buffer |= (b & 0xFF);
                bitsLeft += 8;
                while (bitsLeft >= 5)
                {
                    int index = (buffer >> (bitsLeft - 5)) & 0x1F;
                    sb.Append(Base32Chars[index]);
                    bitsLeft -= 5;
                }
            }
            if (bitsLeft > 0)
            {
                int index = (buffer << (5 - bitsLeft)) & 0x1F;
                sb.Append(Base32Chars[index]);
            }
            return sb.ToString();
        }

        private static byte[] FromBase32(string base32)
        {
            var bytes = new System.Collections.Generic.List<byte>();
            int buffer = 0;
            int bitsLeft = 0;

            foreach (char c in base32)
            {
                int index = Base32Chars.IndexOf(c);
                if (index < 0) throw new ArgumentException($"Ungültiges Zeichen im Base32 String: {c}");

                buffer <<= 5;
                buffer |= index;
                bitsLeft += 5;
                if (bitsLeft >= 8)
                {
                    bytes.Add((byte)((buffer >> (bitsLeft - 8)) & 0xFF));
                    bitsLeft -= 8;
                }
            }
            return bytes.ToArray();
        }
    }
}
