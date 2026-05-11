using System;
using System.DirectoryServices;
using System.DirectoryServices.ActiveDirectory;
using System.Security.Cryptography;
using System.Text;

namespace EZKPM.Client.Desktop.Services
{
    public static class AdBackupService
    {
        public static void BackupKeyToAd(string encryptedKeyBlob)
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
                
                // Wir verwenden die Maschinenspezifische Identifikation als Container-Namen
                // z.B. CN=MasterKey-DESKTOP123
                string objectName = $"CN=MasterKey-{Environment.MachineName}";

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
                    Program.LogDebug($"[AdBackupService] Master Key in AD gesichert unter {objectName}");
                    targetChild.Dispose();
                }
            }
            catch (Exception ex)
            {
                Program.LogDebug($"[AdBackupService] Fehler beim Sichern des Keys ins AD: {ex.Message}");
            }
        }
    }
}
