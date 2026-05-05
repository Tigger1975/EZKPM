using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace EZKPM.Client.Core.Cryptography
{
    /// <summary>
    /// Implementiert Shamir's Secret Sharing (GF(256)) für das 4-Augen-Prinzip (Break-Glass Recovery).
    /// Zerlegt ein Secret in n Fragmente, von denen m zur Wiederherstellung benötigt werden.
    /// </summary>
    public static class ShamirSecretSharing
    {
        private const int Prime = 257; // Für einfache Implementierung nutzen wir oft eine größere Primzahl oder echtes GF(2^8).
        // Um echte Byte-Arrays sauber zu verarbeiten, nutzen wir GF(2^8) mit dem Standard-Polynom 0x11B.

        private static readonly byte[] LogTable = new byte[256];
        private static readonly byte[] ExpTable = new byte[256];

        static ShamirSecretSharing()
        {
            int poly = 0x11B;
            int x = 1;
            for (int i = 0; i < 255; i++)
            {
                ExpTable[i] = (byte)x;
                LogTable[x] = (byte)i;
                
                // Generator 3
                int x2 = x << 1;
                if ((x2 & 0x100) != 0) x2 ^= poly;
                x ^= x2;
            }
            ExpTable[255] = ExpTable[0];
            LogTable[0] = 0; // Log(0) ist mathematisch undefiniert, wir fangen das in der Logik ab.
        }

        private static byte Add(byte a, byte b) => (byte)(a ^ b);
        private static byte Sub(byte a, byte b) => (byte)(a ^ b);

        private static byte Mul(byte a, byte b)
        {
            if (a == 0 || b == 0) return 0;
            int logA = LogTable[a];
            int logB = LogTable[b];
            int sum = logA + logB;
            return ExpTable[sum % 255];
        }

        private static byte Div(byte a, byte b)
        {
            if (b == 0) throw new DivideByZeroException("GF(2^8) division by zero.");
            if (a == 0) return 0;
            int logA = LogTable[a];
            int logB = LogTable[b];
            int diff = logA - logB;
            if (diff < 0) diff += 255;
            return ExpTable[diff];
        }

        /// <summary>
        /// Zerlegt ein Secret in n Fragmente, wobei m Fragmente (threshold) zur Wiederherstellung benötigt werden.
        /// </summary>
        public static List<byte[]> SplitSecret(byte[] secret, int threshold, int shares)
        {
            if (threshold > shares || threshold < 2)
                throw new ArgumentException("Ungültiger Threshold.");

            var result = new List<byte[]>();
            for (int i = 0; i < shares; i++)
            {
                // Jedes Fragment enthält an Position 0 die X-Koordinate, gefolgt von den Y-Koordinaten.
                byte[] share = new byte[secret.Length + 1];
                share[0] = (byte)(i + 1); // X-Koordinate (1 bis n)
                result.Add(share);
            }

            for (int byteIdx = 0; byteIdx < secret.Length; byteIdx++)
            {
                byte[] poly = new byte[threshold];
                poly[0] = secret[byteIdx];
                for (int i = 1; i < threshold; i++)
                {
                    poly[i] = (byte)RandomNumberGenerator.GetInt32(1, 256);
                }

                for (int i = 0; i < shares; i++)
                {
                    byte x = (byte)(i + 1);
                    byte y = EvalPoly(poly, x);
                    result[i][byteIdx + 1] = y;
                }
            }

            return result;
        }

        /// <summary>
        /// Stellt das Secret aus m Fragmenten wieder her (Lagrange Interpolation).
        /// </summary>
        public static byte[] RecoverSecret(List<byte[]> shares)
        {
            if (shares == null || shares.Count < 2)
                throw new ArgumentException("Zu wenige Fragmente.");

            int secretLength = shares[0].Length - 1;
            byte[] secret = new byte[secretLength];

            for (int byteIdx = 0; byteIdx < secretLength; byteIdx++)
            {
                byte y_intercept = 0;
                for (int i = 0; i < shares.Count; i++)
                {
                    byte x_i = shares[i][0];
                    byte y_i = shares[i][byteIdx + 1];

                    byte numerator = 1;
                    byte denominator = 1;

                    for (int j = 0; j < shares.Count; j++)
                    {
                        if (i == j) continue;
                        byte x_j = shares[j][0];
                        
                        // Lagrange Basispolynom berechnen: product( (0 - x_j) / (x_i - x_j) )
                        // Im GF(2^8) ist Subtraktion gleich Addition (XOR), also 0 - x_j = x_j
                        numerator = Mul(numerator, x_j);
                        denominator = Mul(denominator, Sub(x_i, x_j));
                    }

                    byte lagrangePolynomial = Div(numerator, denominator);
                    y_intercept = Add(y_intercept, Mul(y_i, lagrangePolynomial));
                }
                secret[byteIdx] = y_intercept;
            }

            return secret;
        }

        private static byte EvalPoly(byte[] poly, byte x)
        {
            byte result = 0;
            for (int i = poly.Length - 1; i >= 0; i--)
            {
                result = Add(Mul(result, x), poly[i]);
            }
            return result;
        }
    }
}
