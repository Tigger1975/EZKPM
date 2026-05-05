using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using KeePassLib;
using KeePassLib.Keys;
using KeePassLib.Serialization;
using EZKPM.Shared.Contracts;

namespace EZKPM.Client.Core.Services
{
    public class KdbxImporter : IPasswordDbImporter
    {
        public Task<List<VaultAssetPayload>> ImportAsync(Stream fileStream, string password = null, string keyFilePath = null)
        {
            var resultList = new List<VaultAssetPayload>();
            try
            {
                if (string.IsNullOrEmpty(password))
                {
                    throw new Exception("Für den Import einer KDBX-Datei wird ein Master-Passwort benötigt.");
                }

                // Copy stream to a temp file because KeePassLib works best with file paths for IOConnectionInfo
                var tempPath = Path.GetTempFileName() + ".kdbx";
                using (var fs = new FileStream(tempPath, FileMode.Create))
                {
                    fileStream.CopyTo(fs);
                }

                var ioConnInfo = new IOConnectionInfo { Path = tempPath };
                var compKey = new CompositeKey();
                
                if (!string.IsNullOrEmpty(password))
                {
                    compKey.AddUserKey(new KcpPassword(password));
                }

                if (!string.IsNullOrEmpty(keyFilePath) && File.Exists(keyFilePath))
                {
                    compKey.AddUserKey(new KcpKeyFile(keyFilePath));
                }

                var db = new PwDatabase();
                db.Open(ioConnInfo, compKey, null);

                if (db.RootGroup != null)
                {
                    ParseGroup(db.RootGroup, null, resultList);
                }

                db.Close();
                File.Delete(tempPath);
            }
            catch (Exception ex)
            {
                throw new Exception($"Fehler beim Einlesen der .kdbx Datei: {ex.Message}", ex);
            }

            return Task.FromResult(resultList);
        }

        private void ParseGroup(PwGroup group, Guid? parentFolderId, List<VaultAssetPayload> resultList)
        {
            var folderId = Guid.NewGuid();
            var folderAsset = new VaultAssetPayload
            {
                TransientAssetId = folderId,
                ParentFolderId = parentFolderId,
                AssetType = "Folder",
                Title = string.IsNullOrEmpty(group.Name) ? "Unnamed Folder" : group.Name
            };
            
            resultList.Add(folderAsset);

            foreach (var entry in group.Entries)
            {
                var payload = ParseEntry(entry);
                payload.ParentFolderId = folderId;
                resultList.Add(payload);
            }

            foreach (var subGroup in group.Groups)
            {
                ParseGroup(subGroup, folderId, resultList);
            }
        }

        private VaultAssetPayload ParseEntry(PwEntry entry)
        {
            var payload = new VaultAssetPayload
            {
                TransientAssetId = Guid.NewGuid(),
                AssetType = "Login",
                Title = entry.Strings.ReadSafe("Title"),
                Username = entry.Strings.ReadSafe("UserName"),
                Password = entry.Strings.ReadSafe("Password"),
                Url = entry.Strings.ReadSafe("URL"),
                Notes = entry.Strings.ReadSafe("Notes")
            };

            foreach (var kvp in entry.Strings)
            {
                var key = kvp.Key;
                if (key != "Title" && key != "UserName" && key != "Password" && key != "URL" && key != "Notes")
                {
                    if (!string.IsNullOrEmpty(key) && kvp.Value != null)
                    {
                        payload.CustomFields.Add(new CustomField
                        {
                            Name = key,
                            Value = kvp.Value.ReadString(),
                            IsSecret = kvp.Value.IsProtected
                        });
                    }
                }
            }

            return payload;
        }
    }
}
