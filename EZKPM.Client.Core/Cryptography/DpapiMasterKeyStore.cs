using System;
using System.IO;
using System.Security.Cryptography;

namespace EZKPM.Client.Core.Cryptography
{
    /// <summary>
    /// Implementiert das Bootstrapping des Master-Keys.
    /// Nutzt die Windows Data Protection API (DPAPI), um den asymmetrischen Private Key
    /// sicher an die Windows-Anmeldung des Benutzers zu binden (Alternative zu TPM/FIDO2).
    /// </summary>
    public static class DpapiMasterKeyStore
    {
        private const string AppFolderName = "EZKPM";
        private const string KeyFileName = "machinesecret.dat";

        public static bool HasMachineSecret()
        {
            if (!OperatingSystem.IsWindows()) return true; // Mock true for non-windows
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return File.Exists(Path.Combine(appData, AppFolderName, KeyFileName));
        }

        public static byte[] GetOrGenerateMachineSecret()
        {
            if (!OperatingSystem.IsWindows())
            {
                // Fallback für nicht-Windows (z. B. macOS/Linux in der Entwicklung)
                // Hier würde man den Keychain verwenden. Für den Moment mocken wir das.
                return new byte[16];
            }

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appDir = Path.Combine(appData, AppFolderName);
            var keyPath = Path.Combine(appDir, KeyFileName);

            if (File.Exists(keyPath))
            {
                try
                {
                    byte[] encryptedKey = File.ReadAllBytes(keyPath);
                    // Unprotect entschlüsselt mit dem Windows-Passwort (Current User)
                    byte[] decryptedKey = ProtectedData.Unprotect(encryptedKey, null, DataProtectionScope.CurrentUser);
                    
                    if (decryptedKey.Length == 64 || decryptedKey.Length == 16)
                    {
                        return decryptedKey;
                    }
                }
                catch (CryptographicException)
                {
                    // DPAPI Unprotect failed (e.g. password changed or corrupted).
                    // We must NOT generate a new key, as this would lock the user out of existing assets!
                    throw new RequiresRecoveryException("DPAPI Master-Key konnte nicht entschlüsselt werden. Ein 4-Augen-Recovery wird benötigt.");
                }
            }

            // Wenn wir hier sind, existiert kein Key oder er ist defekt. Wir generieren einen neuen.
            byte[] newMachineSecret = new byte[16];
            RandomNumberGenerator.Fill(newMachineSecret);

            SaveMachineSecret(newMachineSecret);

            return newMachineSecret;
        }

        public static void SaveMachineSecret(byte[] key)
        {
            if (!OperatingSystem.IsWindows()) return;

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appDir = Path.Combine(appData, AppFolderName);
            var keyPath = Path.Combine(appDir, KeyFileName);

            if (!Directory.Exists(appDir))
            {
                Directory.CreateDirectory(appDir);
            }

            byte[] encryptedKey = ProtectedData.Protect(key, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(keyPath, encryptedKey);
        }

        public static void ClearMachineSecret()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var keyPath = Path.Combine(appData, AppFolderName, KeyFileName);
            if (File.Exists(keyPath)) File.Delete(keyPath);
        }
    }
}
