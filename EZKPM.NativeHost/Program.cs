using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;

namespace EZKPM.NativeHost
{
    class Program
    {
        private static string LogPath = @"C:\Users\adm-kh\ezkpm_nativehost.log";

        static async Task Main(string[] args)
        {
            // Verbot von Console.WriteLine! Logs gehen in eine Datei.
            Log("EZKPM Native Messaging Host started.");

            try
            {
                while (true)
                {
                    // 1. Lese Request vom Browser (Chrome/Edge) über stdin
                    string requestJson = NativeMessagingProtocol.Read();
                    if (string.IsNullOrEmpty(requestJson))
                    {
                        Log("Received empty or null message. Exiting.");
                        break;
                    }

                    Log($"Received message from browser: {requestJson}");

                    // 2. Sende Request an den laufenden Desktop-Client via Named Pipe
                    string responseJson = await SendToDesktopClient(requestJson);

                    // 3. Sende Antwort zurück an den Browser über stdout
                    NativeMessagingProtocol.Write(responseJson);
                    Log("Sent response back to browser.");
                }
            }
            catch (Exception ex)
            {
                Log($"CRITICAL ERROR: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static async Task<string> SendToDesktopClient(string jsonMessage)
        {
            try
            {
                using var pipeClient = new NamedPipeClientStream(".", "EZKPM_BrowserBridge_Pipe", PipeDirection.InOut, PipeOptions.Asynchronous);
                
                // Kurzer Timeout, da der Client laufen MUSS, sonst klappt Autofill nicht
                await pipeClient.ConnectAsync(2000); 

                using var reader = new StreamReader(pipeClient, Encoding.UTF8, leaveOpen: true);
                using var writer = new StreamWriter(pipeClient, Encoding.UTF8, leaveOpen: true);
                
                writer.AutoFlush = true;
                await writer.WriteLineAsync(jsonMessage);

                string response = await reader.ReadLineAsync();
                return response ?? "{\"error\": \"Empty response from desktop client\"}";
            }
            catch (TimeoutException)
            {
                return "{\"error\": \"Desktop Client is not running or locked\"}";
            }
            catch (Exception ex)
            {
                Log($"Pipe Error: {ex.Message}");
                return $"{{\"error\": \"Failed to connect to vault: {ex.Message}\"}}";
            }
        }

        private static void Log(string message)
        {
            try
            {
                File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
            catch { }
        }
    }
}
