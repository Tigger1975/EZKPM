using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace EZKPM.Client.Desktop.Services
{
    public class AppConfig
    {
        public string ServerUrl { get; set; } = "";
        public DateTime LastVulnerabilityScan { get; set; } = DateTime.MinValue;
        public string SmtpServer { get; set; } = "";
        public int SmtpPort { get; set; } = 25;
        public string SmtpSender { get; set; } = "";
        public string SmtpSubjectTemplate { get; set; } = "Ihre Einladung für EZKPM / Your invitation for EZKPM (Ironclad Vault)";
        public string SmtpBodyTemplate { get; set; } = "Hallo {DisplayName},\n\nSie wurden zur Nutzung von EZKPM (Ironclad Vault) eingeladen.\n1. Laden Sie den Client hier herunter: {ServerUrl}\n2. Klicken Sie nach der Installation auf folgenden Link, um sich zu verbinden:\n   ezkpm://pair?code={PairingCode}\n\nAlternativ können Sie den Code manuell eingeben: {PairingCode}\n\n---\n\nHello {DisplayName},\n\nYou have been invited to use EZKPM (Ironclad Vault).\n1. Download the client here: {ServerUrl}\n2. After installation, click the following link to connect:\n   ezkpm://pair?code={PairingCode}\n\nAlternatively, you can enter the code manually: {PairingCode}";
    }

    public static class ConfigurationManager
    {
        private const string ConfigFileName = "config.json";
        private static string ConfigPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EZKPM", ConfigFileName);

        public static AppConfig CurrentConfig { get; private set; } = new AppConfig();

        public static void LoadConfig(string[] args)
        {
            // 1. Check command line arguments first (--server="http://192.168.1.100:5000")
            string cliServer = null;
            foreach (var arg in args)
            {
                if (arg.StartsWith("--server=", StringComparison.OrdinalIgnoreCase))
                {
                    cliServer = arg.Substring("--server=".Length).Trim('"', '\'');
                }
            }

            if (!string.IsNullOrEmpty(cliServer))
            {
                CurrentConfig.ServerUrl = EnsureValidUrl(cliServer);
                Program.LogDebug($"Server URL overridden by CLI: {CurrentConfig.ServerUrl}");
                SaveConfig(); // Optionally save it or just use it. We'll save it as the new default.
                return;
            }

            // 2. Load from file
            if (File.Exists(ConfigPath))
            {
                try
                {
                    string json = File.ReadAllText(ConfigPath);
                    var config = JsonSerializer.Deserialize<AppConfig>(json);
                    if (config != null && !string.IsNullOrWhiteSpace(config.ServerUrl))
                    {
                        CurrentConfig = config;
                        Program.LogDebug($"Loaded Server URL from config: {CurrentConfig.ServerUrl}");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Program.LogDebug($"Failed to load config: {ex.Message}");
                }
            }
        }

        public static void SaveConfig()
        {
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string json = JsonSerializer.Serialize(CurrentConfig, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception ex)
            {
                Program.LogDebug($"Failed to save config: {ex.Message}");
            }
        }

        public static async Task<bool> IsServerReachableAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;

            try
            {
                // Simple health check endpoint. If not implemented on server, just hitting root / or catching 404 is enough to prove the server is answering.
                var handler = new HttpClientHandler {  };
                using var client = new HttpClient(handler);
                client.Timeout = TimeSpan.FromSeconds(3); // Fast timeout
                
                // We just send a GET to /swagger or root to check if a TCP connection is established and HTTP is spoken
                var response = await client.GetAsync($"{url.TrimEnd('/')}/");
                return true; 
            }
            catch
            {
                return false;
            }
        }

        public static string EnsureValidUrl(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "";
            input = input.Trim();
            if (!input.StartsWith("http://") && !input.StartsWith("https://"))
            {
                input = "https://" + input;
            }
            return input.TrimEnd('/');
        }
    }
}
