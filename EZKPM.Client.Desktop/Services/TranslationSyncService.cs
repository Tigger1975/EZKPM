using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace EZKPM.Client.Desktop.Services
{
    public class TranslationSyncService
    {
        public static void SyncTranslations()
        {
            try
            {
                // Da die App in bin/Debug/net8.0/ läuft, navigieren wir 3 Ebenen hoch zum Projekt-Root
                var currentDir = AppDomain.CurrentDomain.BaseDirectory;
                var projectRoot = Directory.GetParent(currentDir)?.Parent?.Parent?.Parent?.FullName;

                if (projectRoot == null || !Directory.Exists(Path.Combine(projectRoot, "Views"))) return;

                var resxPath = Path.Combine(projectRoot, "Resources", "AppStrings.resx");
                if (!File.Exists(resxPath))
                {
                    Program.LogDebug("[I18N] AppStrings.resx nicht gefunden. Auto-Sync abgebrochen.");
                    return;
                }

                // 1. Suche nach allen {Binding [Key]} Einträgen im XAML
                var keysInUse = new HashSet<string>();
                var regex = new Regex(@"\{Binding \[([a-zA-Z0-9_]+)\]"); // Extrahiert den Keynamen

                var axamlFiles = Directory.GetFiles(projectRoot, "*.axaml", SearchOption.AllDirectories);
                foreach (var file in axamlFiles)
                {
                    var content = File.ReadAllText(file);
                    var matches = regex.Matches(content);
                    foreach (Match match in matches)
                    {
                        keysInUse.Add(match.Groups[1].Value);
                    }
                }

                // 2. Lese aktuelle Keys aus der ResX (via XML, damit wir nichts kaputt machen)
                var doc = XDocument.Load(resxPath);
                var existingKeys = doc.Root.Elements("data").Select(e => e.Attribute("name")?.Value).ToHashSet();

                // 3. Finde fehlende Keys
                var missingKeys = keysInUse.Except(existingKeys).ToList();

                // 4. Schreibe neue Keys rein
                if (missingKeys.Any())
                {
                    foreach (var key in missingKeys)
                    {
                        var dataElement = new XElement("data",
                            new XAttribute("name", key),
                            new XAttribute("xml:space", "preserve"));

                        dataElement.Add(new XElement("value", $"TODO: {key}")); // Auto-Generierter Text
                        doc.Root.Add(dataElement);

                        Program.LogDebug($"[I18N Auto-Sync] Neuen Schlüssel hinzugefügt: {key}");
                    }
                    doc.Save(resxPath);
                    Program.LogDebug($"✅ [I18N] Auto-Sync: {missingKeys.Count} neue Schlüssel zur AppStrings.resx hinzugefügt.");
                }
                else
                {
                    Program.LogDebug("✅ [I18N] Auto-Sync: ResX ist aktuell. Keine fehlenden Texte gefunden.");
                }
            }
            catch (Exception ex)
            {
                Program.LogDebug($"⚠️ [I18N] Fehler beim automatischen ResX-Sync: {ex.Message}");
            }
        }
    }
}