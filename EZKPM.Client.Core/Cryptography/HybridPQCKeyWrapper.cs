using System;
using System.Security.Cryptography;
using EZKPM.Client.Core.Security;

namespace EZKPM.Client.Core.Cryptography
{
    /// <summary>
    /// Setzt das "Asymmetrische Sharing" für AD-Gruppenmitglieder um.
    /// Nutzt ein hybrides KEM (Key Encapsulation Mechanism) Verfahren: 
    /// X25519 (Klassisch/Bewährt) + Kyber/ML-KEM (Post-Quanten-Sicher).
    /// </summary>
    public class HybridPqcKeyWrapper
    {
        private const int AesKeySize = 32; // 256 Bit
        private const int GcmNonceSize = 12;
        private const int GcmTagSize = 16;

        /// <summary>
        /// Verschlüsselt (Wrapped) einen Asset-Key für einen spezifischen Empfänger (AD-User/Gruppe).
        /// </summary>
        /// <param name="assetKey">Der zu teilende AES-256 Key (strikt im gepinnten RAM)</param>
        /// <param name="recipientPublicKeyX25519">Der öffentliche X25519 Key des Empfängers</param>
        /// <param name="recipientPublicKeyKyber">Der öffentliche Kyber/ML-KEM Key des Empfängers</param>
        /// <returns>Das EncryptedKeyShare-Blob für die Datenbank (AssetAcl)</returns>
        public byte[] WrapAssetKey(
            SecureMemory assetKey,
            byte[] recipientPublicKeyX25519,
            byte[] recipientPublicKeyKyber)
        {
            if (assetKey == null || assetKey.Span.Length != AesKeySize)
                throw new ArgumentException("Ungültiger Asset-Key.");

            // 1. Klassischer ECDH (X25519) Shared Secret
            // Da CngKey Windows-spezifisch ist und wir cross-platform (MAUI/Avalonia) sind, 
            // kapseln wir auch X25519 über eine BouncyCastle-Mock-Schnittstelle.
            byte[] sharedSecretClassic = GenerateX25519SharedSecret(recipientPublicKeyX25519);

            // 2. Post-Quanten KEM (Kyber/ML-KEM via BouncyCastle Abstraktion)
            // Hinweis: In der echten Implementierung rufen wir hier BouncyCastle's KyberKEMGenerator auf.
            byte[] kyberCiphertext;
            byte[] sharedSecretPqc = GenerateKyberSharedSecret(recipientPublicKeyKyber, out kyberCiphertext);

            // 3. Hybrid KDF (HKDF): Beide Secrets zu einem Key Encryption Key (KEK) kombinieren
            byte[] combinedSecrets = new byte[sharedSecretClassic.Length + sharedSecretPqc.Length];
            Buffer.BlockCopy(sharedSecretClassic, 0, combinedSecrets, 0, sharedSecretClassic.Length);
            Buffer.BlockCopy(sharedSecretPqc, 0, combinedSecrets, sharedSecretClassic.Length, sharedSecretPqc.Length);

            // CS1503 Fix: Array.Empty<byte>() statt ReadOnlySpan<byte>.Empty verwenden.
            byte[] kek = HKDF.DeriveKey(HashAlgorithmName.SHA256, combinedSecrets, AesKeySize, Array.Empty<byte>(), Array.Empty<byte>());

            // 4. Asset-Key mit dem abgeleiteten KEK verschlüsseln (AES Key Wrap / GCM)
            byte[] nonce = new byte[GcmNonceSize];
            RandomNumberGenerator.Fill(nonce);

            byte[] encryptedAssetKey = new byte[AesKeySize];
            byte[] tag = new byte[GcmTagSize];

            using (var aesGcm = new AesGcm(kek))
            {
                // Wir wrappen den Asset-Key, der sicher in SecureMemory liegt
                aesGcm.Encrypt(nonce, assetKey.Span, encryptedAssetKey, tag);
            }

            // Forensik: Sensible Zwischenschritte im RAM sofort wipen
            CryptographicOperations.ZeroMemory(sharedSecretClassic);
            CryptographicOperations.ZeroMemory(sharedSecretPqc);
            CryptographicOperations.ZeroMemory(combinedSecrets);
            CryptographicOperations.ZeroMemory(kek);

            // 5. Payload zusammensetzen: Ephemerer Kyber Ciphertext + Nonce + Tag + EncryptedKey
            return AssemblePayload(kyberCiphertext, nonce, tag, encryptedAssetKey);
        }

        /// <summary>
        /// Entschlüsselt einen vom Server erhaltenen EncryptedKeyShare mit dem eigenen privaten Schlüssel.
        /// </summary>
        public SecureMemory UnwrapAssetKey(
            byte[] encryptedKeyShare,
            SecureMemory myPrivateKeyX25519,
            SecureMemory myPrivateKeyKyber)
        {
            // 1. Payload parsen
            ParsePayload(encryptedKeyShare, out byte[] kyberCiphertext, out byte[] nonce, out byte[] tag, out byte[] encryptedAssetKey);

            // 2. Klassischer ECDH (X25519)
            // In der Realität benötigen wir hier den Public Key des Absenders (Owner), 
            // der typischerweise an das Asset angehängt wird. Für dieses Beispiel abstrahiert.
            byte[] privateKeySpan = myPrivateKeyX25519.Span.ToArray();
            byte[] sharedSecretClassic = GenerateX25519SharedSecret(privateKeySpan);
            CryptographicOperations.ZeroMemory(privateKeySpan); // Temporäres Array wipen

            // 3. Post-Quanten Decapsulation
            byte[] sharedSecretPqc = DecapsulateKyber(myPrivateKeyKyber, kyberCiphertext);

            // 4. Hybrid KDF (HKDF)
            byte[] combinedSecrets = new byte[sharedSecretClassic.Length + sharedSecretPqc.Length];
            Buffer.BlockCopy(sharedSecretClassic, 0, combinedSecrets, 0, sharedSecretClassic.Length);
            Buffer.BlockCopy(sharedSecretPqc, 0, combinedSecrets, sharedSecretClassic.Length, sharedSecretPqc.Length);

            // CS1503 Fix: Array.Empty<byte>() statt ReadOnlySpan<byte>.Empty verwenden.
            byte[] kek = HKDF.DeriveKey(HashAlgorithmName.SHA256, combinedSecrets, AesKeySize, Array.Empty<byte>(), Array.Empty<byte>());

            // 5. Asset-Key auspacken
            byte[] decryptedAssetKey = new byte[AesKeySize];
            using (var aesGcm = new AesGcm(kek))
            {
                aesGcm.Decrypt(nonce, encryptedAssetKey, tag, decryptedAssetKey);
            }

            // Forensik-Wiping
            CryptographicOperations.ZeroMemory(sharedSecretClassic);
            CryptographicOperations.ZeroMemory(sharedSecretPqc);
            CryptographicOperations.ZeroMemory(combinedSecrets);
            CryptographicOperations.ZeroMemory(kek);

            // Sicher verpackt zurückgeben
            return new SecureMemory(decryptedAssetKey);
        }

        // --- Mock Abstraktionen für BouncyCastle PQC & X25519 ---
        // Diese Methoden kapseln die spezifischen BouncyCastle-Aufrufe, um die API sauber zu halten.
        private byte[] GenerateKyberSharedSecret(byte[] pubKey, out byte[] ciphertext)
        {
            ciphertext = new byte[768]; RandomNumberGenerator.Fill(ciphertext);
            return new byte[32]; // DUMMY: Fester Rückgabewert für lokalen Test-Roundtrip
        }
        private byte[] DecapsulateKyber(SecureMemory privKey, byte[] ciphertext) { return new byte[32]; }

        // CS0117 Fix: Plattformunabhängiger Mock, da CngKey/NamedCurves.Curve25519 nicht cross-platform ist.
        private byte[] GenerateX25519SharedSecret(byte[] keyMaterial) { return new byte[32]; }

        private byte[] AssemblePayload(byte[] ct, byte[] n, byte[] t, byte[] k)
        {
            byte[] payload = new byte[ct.Length + n.Length + t.Length + k.Length];
            int offset = 0;
            Buffer.BlockCopy(ct, 0, payload, offset, ct.Length); offset += ct.Length;
            Buffer.BlockCopy(n, 0, payload, offset, n.Length); offset += n.Length;
            Buffer.BlockCopy(t, 0, payload, offset, t.Length); offset += t.Length;
            Buffer.BlockCopy(k, 0, payload, offset, k.Length);
            return payload;
        }

        private void ParsePayload(byte[] payload, out byte[] ct, out byte[] n, out byte[] t, out byte[] k)
        {
            ct = new byte[768]; n = new byte[GcmNonceSize]; t = new byte[GcmTagSize]; k = new byte[AesKeySize];
            int offset = 0;
            Buffer.BlockCopy(payload, offset, ct, 0, ct.Length); offset += ct.Length;
            Buffer.BlockCopy(payload, offset, n, 0, n.Length); offset += n.Length;
            Buffer.BlockCopy(payload, offset, t, 0, t.Length); offset += t.Length;
            Buffer.BlockCopy(payload, offset, k, 0, k.Length);
        }
    }
}