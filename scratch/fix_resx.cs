using System;
using System.IO;
using System.Text.RegularExpressions;

class Program
{
    static void Main()
    {
        var basePath = @"c:\Users\adm-kh\source\repos\EZKPM\EZKPM.Client.Desktop\Resources";
        var resx = Path.Combine(basePath, "AppStrings.resx");
        var deResx = Path.Combine(basePath, "AppStrings.de.resx");
        var csFile = Path.Combine(basePath, "AppStrings.Designer.cs");

        var strings = new (string Key, string En, string De)[]
        {
            ("AuthReason_CopyPassword", "Copy Password", "Passwort kopieren"),
            ("AuthReason_CopyAllDetails", "Copy All Details", "Alle Details kopieren"),
            ("AuthReason_ShowPassword", "Show Password in Plaintext", "Passwort im Klartext anzeigen"),
            ("AuthReason_CopyTotpSecret", "Copy TOTP Secret", "TOTP Secret kopieren"),
            ("AuthReason_EditVault", "Edit/Save Vault", "Tresor bearbeiten/speichern"),
            ("AuthReason_AutoType", "Execute Auto-Type", "Auto-Type ausführen"),
            ("AuthReason_RotationAssistant", "Open Rotation Assistant", "Rotation Assistant öffnen"),
            ("AuthReason_AdminPanel", "Open Admin Panel", "Admin Panel öffnen")
        };

        // Add to Resx
        void AppendToResx(string file, Func<(string Key, string En, string De), string> valSelector)
        {
            var content = File.ReadAllText(file);
            foreach (var s in strings)
            {
                if (!content.Contains($"name=\"{s.Key}\""))
                {
                    string xml = $@"  <data name=""{s.Key}"" xml:space=""preserve"">
    <value>{valSelector(s)}</value>
  </data>
";
                    content = content.Replace("</root>", xml + "</root>");
                }
            }
            File.WriteAllText(file, content);
        }

        AppendToResx(resx, s => s.En);
        AppendToResx(deResx, s => s.De);

        // Add to C#
        var csContent = File.ReadAllText(csFile);
        foreach (var s in strings)
        {
            if (!csContent.Contains($"public static string {s.Key}"))
            {
                string prop = $@"        public static string {s.Key} {{
            get {{
                return ResourceManager.GetString(""{s.Key}"", resourceCulture);
            }}
        }}
        
";
                int idx = csContent.LastIndexOf("}");
                idx = csContent.LastIndexOf("}", idx - 1);
                csContent = csContent.Insert(idx, prop);
            }
        }
        File.WriteAllText(csFile, csContent);
        Console.WriteLine("Done.");
    }
}
