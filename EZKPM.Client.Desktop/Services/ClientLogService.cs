using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace EZKPM.Client.Desktop.Services
{
    public class ClientLogDto
    {
        public string MachineName { get; set; }
        public string Username { get; set; }
        public string Level { get; set; }
        public string Message { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public static class ClientLogService
    {
        private static readonly string LogBufferFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EZKPM", "log_buffer.json");
        private static readonly object _lock = new object();
        private static Timer _flushTimer;
        private static HttpClient _httpClient;

        public static void Initialize()
        {
            var handler = new HttpClientHandler {  ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true };
            _httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
            
            _flushTimer = new Timer(async _ => await FlushLogsAsync(), null, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(5));
        }

        public static void EnqueueLog(string level, string message)
        {
            try
            {
                var logEntry = new ClientLogDto
                {
                    MachineName = Environment.MachineName,
                    Username = Environment.UserName,
                    Level = level,
                    Message = message,
                    Timestamp = DateTime.UtcNow
                };

                List<ClientLogDto> currentLogs = new List<ClientLogDto>();
                lock (_lock)
                {
                    if (File.Exists(LogBufferFile))
                    {
                        try
                        {
                            var json = File.ReadAllText(LogBufferFile);
                            var existing = JsonSerializer.Deserialize<List<ClientLogDto>>(json);
                            if (existing != null) currentLogs = existing;
                        }
                        catch { }
                    }

                    currentLogs.Add(logEntry);
                    
                    // Keep buffer from growing infinitely if server is offline for months
                    if (currentLogs.Count > 10000)
                    {
                        currentLogs.RemoveRange(0, currentLogs.Count - 10000);
                    }

                    File.WriteAllText(LogBufferFile, JsonSerializer.Serialize(currentLogs));
                }
            }
            catch { } // Best effort
        }

        private static string _cachedEnvPubKey = null;

        public static async Task FlushLogsAsync()
        {
            try
            {
                string serverUrl = ConfigurationManager.CurrentConfig.ServerUrl;
                if (string.IsNullOrEmpty(serverUrl)) return;

                List<ClientLogDto> logsToSend = null;
                lock (_lock)
                {
                    if (!File.Exists(LogBufferFile)) return;
                    try
                    {
                        var json = File.ReadAllText(LogBufferFile);
                        logsToSend = JsonSerializer.Deserialize<List<ClientLogDto>>(json);
                    }
                    catch { }
                }

                if (logsToSend == null || logsToSend.Count == 0) return;

                // Try to get EnvPubKey
                if (string.IsNullOrEmpty(_cachedEnvPubKey))
                {
                    var envResponse = await _httpClient.GetAsync($"{serverUrl}/api/v1/log/envkey");
                    if (envResponse.IsSuccessStatusCode)
                    {
                        var envResult = await envResponse.Content.ReadFromJsonAsync<JsonElement>();
                        if (envResult.TryGetProperty("publicKey", out var pub) || envResult.TryGetProperty("PublicKey", out pub))
                        {
                            _cachedEnvPubKey = pub.GetString();
                        }
                    }
                }

                // Encrypt logs if key is available
                if (!string.IsNullOrEmpty(_cachedEnvPubKey))
                {
                    using var rsa = System.Security.Cryptography.RSA.Create();
                    rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(_cachedEnvPubKey), out _);

                    foreach (var log in logsToSend)
                    {
                        if (log.Message.StartsWith("ENV_ENC:")) continue;

                        byte[] aesKey = new byte[32];
                        byte[] nonce = new byte[12];
                        System.Security.Cryptography.RandomNumberGenerator.Fill(aesKey);
                        System.Security.Cryptography.RandomNumberGenerator.Fill(nonce);

                        byte[] plainText = System.Text.Encoding.UTF8.GetBytes(log.Message);
                        byte[] cipherText = new byte[plainText.Length];
                        byte[] tag = new byte[16];

                        using (var aesGcm = new System.Security.Cryptography.AesGcm(aesKey, 16))
                        {
                            aesGcm.Encrypt(nonce, plainText, cipherText, tag);
                        }

                        byte[] encryptedAesKey = rsa.Encrypt(aesKey, System.Security.Cryptography.RSAEncryptionPadding.OaepSHA256);

                        string payload = $"{Convert.ToBase64String(encryptedAesKey)}:{Convert.ToBase64String(nonce)}:{Convert.ToBase64String(cipherText)}:{Convert.ToBase64String(tag)}";
                        log.Message = $"ENV_ENC:{payload}";
                    }
                }

                var url = $"{serverUrl}/api/v1/log/batch";
                var response = await _httpClient.PostAsJsonAsync(url, logsToSend);
                
                if (response.IsSuccessStatusCode)
                {
                    lock (_lock)
                    {
                        // Successfully sent, clear the buffer
                        if (File.Exists(LogBufferFile))
                        {
                            File.Delete(LogBufferFile);
                        }
                    }
                }
            }
            catch
            {
                // Server unreachable or error, keep buffered
            }
        }
    }
}
