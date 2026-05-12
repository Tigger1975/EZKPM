using System;
using System.DirectoryServices;
using System.DirectoryServices.ActiveDirectory;
using System.Security.Cryptography;
using System.Text;

namespace EZKPM.Client.Desktop.Services
{
    public static class AdKeyStorageService
    {
        private static string GetObjectName()
        {
            string sid = "S-1-5-21-DUMMY";
            if (OperatingSystem.IsWindows())
            {
                sid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? sid;
            }
            return $"CN=MasterKey-{Environment.MachineName}-{sid}";
        }

        public static string RetrieveKeyFromAd()
        {
            if (!OperatingSystem.IsWindows()) return null;

            try
            {
                Domain currentDomain = Domain.GetCurrentDomain();
                if (currentDomain == null) return null;

                string domainName = currentDomain.Name;
                string[] dcParts = domainName.Split('.');
                string dcPath = "DC=" + string.Join(",DC=", dcParts);
                
                string containerPath = $"LDAP://OU=EZKPM-Keys,{dcPath}";
                string objectName = GetObjectName();

                using var entry = new DirectoryEntry(containerPath);
                
                try
                {
                    using var targetChild = entry.Children.Find(objectName, "contact");
                    if (targetChild.Properties.Contains("info") && targetChild.Properties["info"].Count > 0)
                    {
                        return targetChild.Properties["info"][0]?.ToString();
                    }
                }
                catch (System.Runtime.InteropServices.COMException ex) when (ex.ErrorCode == unchecked((int)0x80072030))
                {
                    // Not found
                }
            }
            catch (Exception ex)
            {
                Program.LogDebug($"[AdKeyStorageService] Error retrieving key from AD: {ex.Message}");
            }
            return null;
        }

        public static void StoreKeyInAd(string encryptedKeyBlob)
        {
            if (!OperatingSystem.IsWindows()) return;

            try
            {
                // Verify we are in a domain context
                Domain currentDomain = Domain.GetCurrentDomain();
                if (currentDomain == null) return;

                string domainName = currentDomain.Name;
                string[] dcParts = domainName.Split('.');
                string dcPath = "DC=" + string.Join(",DC=", dcParts);
                
                string containerPath = $"LDAP://OU=EZKPM-Keys,{dcPath}";
                string objectName = GetObjectName();

                using var entry = new DirectoryEntry(containerPath);
                
                DirectoryEntry targetChild = null;
                
                // Prüfen ob das Objekt bereits existiert
                try
                {
                    targetChild = entry.Children.Find(objectName, "contact");
                }
                catch (System.Runtime.InteropServices.COMException ex) when (ex.ErrorCode == unchecked((int)0x80072030))
                {
                    // Objekt existiert nicht (0x80072030 = ERROR_DS_NO_SUCH_OBJECT)
                    targetChild = entry.Children.Add(objectName, "contact");
                    targetChild.Properties["description"].Add("EZKPM Machine Blind Drop Backup");
                }

                if (targetChild != null)
                {
                    // Update den Key in 'info' (Notizen-Feld, unterstützt große Strings)
                    targetChild.Properties["info"].Value = encryptedKeyBlob;
                    
                    // Speichern (erfordert CreateChild auf der OU und WriteProperty auf dem Contact)
                    targetChild.CommitChanges();
                    Program.LogDebug($"[AdKeyStorageService] Master Key in AD gesichert unter {objectName}");
                    targetChild.Dispose();
                }
            }
            catch (Exception ex)
            {
                Program.LogDebug($"[AdKeyStorageService] Fehler beim Sichern des Keys ins AD: {ex.Message}");
            }
        }

        public static void ClearAdKey()
        {
            // Optional: Lösche das AD-Objekt. Für den lokalen Reset ignorieren wir es hier erst einmal.
            Program.LogDebug("[AdKeyStorageService] ClearAdKey aufgerufen. AD Objekt wird beibehalten.");
        }
    }
}
