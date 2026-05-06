using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace EZKPM.Client.Core.Security
{
    public static class LegacyPasswordStore
    {
        private const string AppFolderName = "EZKPM";
        private const string PwdFileName = "legacy_pwd.dat";

        public static string GetLegacyPassword()
        {
            if (!OperatingSystem.IsWindows()) return "";

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var pwdPath = Path.Combine(appData, AppFolderName, PwdFileName);

            if (File.Exists(pwdPath))
            {
                try
                {
                    byte[] encrypted = File.ReadAllBytes(pwdPath);
                    byte[] decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                    string pwd = Encoding.UTF8.GetString(decrypted);
                    CryptographicOperations.ZeroMemory(decrypted);
                    return pwd;
                }
                catch
                {
                    return "";
                }
            }
            return "";
        }

        public static void SaveLegacyPassword(string password)
        {
            if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(password)) return;

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appDir = Path.Combine(appData, AppFolderName);
            var pwdPath = Path.Combine(appDir, PwdFileName);

            if (!Directory.Exists(appDir))
            {
                Directory.CreateDirectory(appDir);
            }

            byte[] pwdBytes = Encoding.UTF8.GetBytes(password);
            byte[] encrypted = ProtectedData.Protect(pwdBytes, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(pwdPath, encrypted);
            CryptographicOperations.ZeroMemory(pwdBytes);
        }
    }
}
