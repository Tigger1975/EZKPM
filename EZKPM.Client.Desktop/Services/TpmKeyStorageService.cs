using System;
using System.IO;
using System.Security.Cryptography;

namespace EZKPM.Client.Desktop.Services
{
    public static class TpmKeyStorageService
    {
        private const string ProviderName = "Microsoft Platform Crypto Provider";
        
        public static string GetTpmBlobPath()
        {
            string appDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EZKPM");
            if (!Directory.Exists(appDir)) Directory.CreateDirectory(appDir);
            
            // Isoliere das TPM Blob pro User SID
            string sid = "S-1-5-21-DUMMY";
            if (OperatingSystem.IsWindows())
            {
                sid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? sid;
            }
            return Path.Combine(appDir, $"tpm_blob_{sid}.dat");
        }

        public static bool IsTpmAvailable()
        {
            if (!OperatingSystem.IsWindows()) return false;
            try
            {
                var provider = new CngProvider(ProviderName);
                return provider != null;
            }
            catch
            {
                return false;
            }
        }

        private static string GetKeyName()
        {
            string sid = "S-1-5-21-DUMMY";
            if (OperatingSystem.IsWindows())
            {
                sid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? sid;
            }
            return $"EZKPM_MasterKey_{sid}";
        }

        private static CngKey GetOrCreateTpmKey()
        {
            string keyName = GetKeyName();
            var provider = new CngProvider(ProviderName);
            
            if (CngKey.Exists(keyName, provider))
            {
                return CngKey.Open(keyName, provider);
            }

            var parameters = new CngKeyCreationParameters
            {
                Provider = provider,
                KeyCreationOptions = CngKeyCreationOptions.None // Bind to current user
            };
            
            // Wir nutzen RSA 2048 für das TPM (ausreichend für das Verschlüsseln eines 32-Byte Peppers)
            parameters.Parameters.Add(new CngProperty("Length", BitConverter.GetBytes(2048), CngPropertyOptions.None));
            
            return CngKey.Create(CngAlgorithm.Rsa, keyName, parameters);
        }

        /// <summary>
        /// Verschlüsselt einen 32-Byte Hardware Pepper mit dem TPM.
        /// </summary>
        public static byte[] ProtectHardwarePepper(byte[] rawPepper)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            
            using var tpmKey = GetOrCreateTpmKey();
            using var rsa = new RSACng(tpmKey);
            
            return rsa.Encrypt(rawPepper, RSAEncryptionPadding.OaepSHA256);
        }

        /// <summary>
        /// Entschlüsselt den Hardware Pepper mit dem TPM. Schlägt fehl, wenn das TPM fehlt oder ausgetauscht wurde.
        /// </summary>
        public static byte[] UnprotectHardwarePepper(byte[] encryptedPepper)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            
            using var tpmKey = GetOrCreateTpmKey();
            using var rsa = new RSACng(tpmKey);
            
            return rsa.Decrypt(encryptedPepper, RSAEncryptionPadding.OaepSHA256);
        }

        public static void StoreTpmBlob(string base64Blob)
        {
            File.WriteAllText(GetTpmBlobPath(), base64Blob);
            Program.LogDebug($"[TpmKeyStorageService] TPM Blob lokal gesichert: {GetTpmBlobPath()}");
        }

        public static string RetrieveTpmBlob()
        {
            string path = GetTpmBlobPath();
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }
            return null;
        }
    }
}
