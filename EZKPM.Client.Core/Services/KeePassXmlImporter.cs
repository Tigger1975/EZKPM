using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using EZKPM.Shared.Contracts;

namespace EZKPM.Client.Core.Services
{
    public class KeePassXmlImporter : IPasswordDbImporter
    {
        public Task<List<VaultAssetPayload>> ImportAsync(Stream fileStream)
        {
            var resultList = new List<VaultAssetPayload>();
            try
            {
                var doc = XDocument.Load(fileStream);
                var rootGroup = doc.Descendants("Root").Elements("Group").FirstOrDefault();

                if (rootGroup != null)
                {
                    // Start parsing from the root group
                    ParseGroup(rootGroup, null, resultList);
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Fehler beim Importieren der KeePass XML: {ex.Message}", ex);
            }

            return Task.FromResult(resultList);
        }

        private void ParseGroup(XElement groupElement, Guid? parentFolderId, List<VaultAssetPayload> resultList)
        {
            var groupNameElement = groupElement.Element("Name");
            string groupName = groupNameElement?.Value ?? "Unnamed Group";

            // Create a folder asset for this group (if we want to mirror folders)
            // Or we just flatten it. Let's create folder assets!
            var folderId = Guid.NewGuid();
            var folderAsset = new VaultAssetPayload
            {
                TransientAssetId = folderId,
                ParentFolderId = parentFolderId,
                AssetType = "Folder",
                Title = groupName
            };
            
            // Skip creating a folder for the absolute root if we don't want to clutter, 
            // but for simplicity we create it.
            resultList.Add(folderAsset);

            // Parse Entries in this group
            foreach (var entryElement in groupElement.Elements("Entry"))
            {
                var payload = ParseEntry(entryElement);
                payload.ParentFolderId = folderId; // Attach to the folder
                resultList.Add(payload);
            }

            // Parse sub-groups
            foreach (var subGroupElement in groupElement.Elements("Group"))
            {
                ParseGroup(subGroupElement, folderId, resultList);
            }
        }

        private VaultAssetPayload ParseEntry(XElement entryElement)
        {
            var payload = new VaultAssetPayload
            {
                TransientAssetId = Guid.NewGuid(),
                AssetType = "Login"
            };

            foreach (var stringElement in entryElement.Elements("String"))
            {
                var key = stringElement.Element("Key")?.Value;
                var value = stringElement.Element("Value")?.Value ?? string.Empty;

                switch (key)
                {
                    case "Title":
                        payload.Title = value;
                        break;
                    case "UserName":
                        payload.Username = value;
                        break;
                    case "Password":
                        payload.Password = value;
                        break;
                    case "URL":
                        payload.Url = value;
                        break;
                    case "Notes":
                        payload.Notes = value;
                        break;
                    default:
                        // KeePass Custom Fields
                        if (!string.IsNullOrEmpty(key))
                        {
                            payload.CustomFields.Add(new CustomField
                            {
                                Name = key,
                                Value = value,
                                IsSecret = false // Defaulting to false, we could try to infer
                            });
                        }
                        break;
                }
            }

            return payload;
        }
    }
}
