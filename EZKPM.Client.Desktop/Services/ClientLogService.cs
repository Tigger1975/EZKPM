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
