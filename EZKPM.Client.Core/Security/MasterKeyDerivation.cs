using System;
using System.Security.Cryptography;
using Konscious.Security.Cryptography;

namespace EZKPM.Client.Core.Security
{
    /// <summary>
    /// Schnittstelle zur Hardware-Ebene (z.B. Windows WebAuthn API oder libfido2).
    /// </summary>
    public interface IFido2HardwareKey
    {
        /// <summary>
        /// Fordert das hmac-secret vom FIDO2 Token an (erfordert physische Berührung/PIN).
        /// </summary>
        byte[] GetHmacSecret(byte[] credentialId, byte[] salt);
    }

    /// <summary>
    /// Setzt Pflichtenheft FA 13 um: Hardware-gebundener Login.
    /// Leitet den Master-Key mittels Argon2id und FIDO2 ab.
    /// </summary>
    public class KeyDerivationService
    {
        private readonly IFido2HardwareKey _fidoKey;

        // Argon2id Parameter für High Security (angepasst an Enterprise-Vorgaben)
        private const int DegreeOfParallelism = 4;
        private const int MemorySizeInKB = 128 * 1024; // 128 MB RAM (verhindert ASIC/GPU Brute-Force)
        private const int Iterations = 4;
        private const int MasterKeyLength = 32; // 256-bit für AES-GCM

        public KeyDerivationService(IFido2HardwareKey fidoKey)
        {
            _fidoKey = fidoKey ?? throw new ArgumentNullException(nameof(fidoKey));
        }

        /// <summary>
        /// Leitet den 256-Bit Master Key ab.
        /// Der Server sieht diesen Key NIEMALS. Er existiert nur im gepinnten RAM.
        /// </summary>
        public SecureMemory DeriveMasterKey(SecureMemory masterPassword, byte[] fidoCredentialId, byte[] userSalt)
        {
            if (masterPassword == null) throw new ArgumentNullException(nameof(masterPassword));
            if (userSalt == null || userSalt.Length < 16) throw new ArgumentException("Salt muss min. 16 Bytes lang sein.");

            // 1. Hardware-Anker abfragen (FA 13)
            // Dies erfordert in der Regel eine physische Aktion des Nutzers am FIDO2-Token.
            byte[] fido2HmacSecret = _fidoKey.GetHmacSecret(fidoCredentialId, userSalt);

            if (fido2HmacSecret == null || fido2HmacSecret.Length < 32)
                throw new CryptographicException("FIDO2 hmac-secret konnte nicht erfolgreich extrahiert werden.");

            // Da die Konscious Argon2-Library ein klassisches byte[] erwartet, 
            // müssen wir den Span kurzzeitig kopieren und manuell wipen.
            byte[] passwordBytes = masterPassword.Span.ToArray();

            try
            {
                using var argon2 = new Argon2id(passwordBytes)
                {
                    Salt = userSalt,
                    DegreeOfParallelism = DegreeOfParallelism,
                    MemorySize = MemorySizeInKB,
                    Iterations = Iterations,

                    // Der kryptografische "Pepper": Bindet den abgeleiteten Key hart an die FIDO2-Hardware.
                    // Ohne den physischen Key nützt das Passwort allein gar nichts.
                    KnownSecret = fido2HmacSecret
                };

                // 2. KDF-Ausführung
                byte[] derivedKeyBytes = argon2.GetBytes(MasterKeyLength);

                // 3. Ergebnis sofort kapseln und schützen
                return new SecureMemory(derivedKeyBytes);
            }
            finally
            {
                // Forensik-Wipe: Das extrahierte Klartext-Array hart überschreiben
                CryptographicOperations.ZeroMemory(passwordBytes);
                CryptographicOperations.ZeroMemory(fido2HmacSecret);
            }
        }
    }
}