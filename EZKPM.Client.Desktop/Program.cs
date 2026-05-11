using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using Avalonia;
using Avalonia.Threading;
using EZKPM.Client.Core.Cryptography;
using EZKPM.Client.Core.Interop;
using EZKPM.Client.Core.Services;
using EZKPM.Client.Desktop.Views;

namespace EZKPM.Client.Desktop
{
    internal class Program
    {
        private static string LogFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EZKPM", "ezkpm_debug.txt");
        private static readonly string LockFileName = "ezkpm_build.lock";
        private static readonly string LockFileDir = Path.GetTempPath();
        private static FileSystemWatcher _killSwitchWatcher;

        public static void LogDebug(string message)
        {
            try 
            { 
                Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath));
                File.AppendAllText(LogFilePath, $"[{DateTime.UtcNow:O}] {message}\n"); 
            } 
            catch { }
        }

        public static void LogNativeHost(string message)
        {
            try 
            { 
                var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EZKPM");
                Directory.CreateDirectory(logDir);
                File.AppendAllText(Path.Combine(logDir, "ezkpm_nativehost.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n"); 
            } 
            catch { }
        }

        public static bool IsAutoStart = false;

        [STAThread]
        public static void Main(string[] args)
        {
            string bootstrapLogPath = Path.Combine(Path.GetTempPath(), "ezkpm_bootstrap.log");
            try 
            {
                File.AppendAllText(bootstrapLogPath, $"[{DateTime.UtcNow:O}] --- APP STARTING ---\n");
                
                InitializeKillSwitch();
                File.AppendAllText(bootstrapLogPath, $"[{DateTime.UtcNow:O}] KillSwitch initialized.\n");

                LogDebug($"App started. Arguments: {string.Join(" ", args)}");

                if (args.Any(a => a.Equals("autostart", StringComparison.OrdinalIgnoreCase) || a.Equals("--autostart", StringComparison.OrdinalIgnoreCase)))
                {
                    IsAutoStart = true;
                    RegisterAutostart();
                    File.AppendAllText(bootstrapLogPath, $"[{DateTime.UtcNow:O}] Autostart registered.\n");
                    LogDebug("Running in autostart mode.");
                }

                if (args.Any(a => a.StartsWith("chrome-extension://") || a.StartsWith("ms-browser-extension://")))
                {
                    LogNativeHost("EZKPM Native Messaging Host proxy started.");
                    File.AppendAllText(bootstrapLogPath, $"[{DateTime.UtcNow:O}] Native Messaging Proxy started.\n");
                    try
                    {
                        Task.Run(() => RunNativeHostProxyAsync()).Wait();
                    }
                    catch (Exception ex)
                    {
                        File.AppendAllText(bootstrapLogPath, $"[{DateTime.UtcNow:O}] FATAL NATIVE HOST ERROR: {ex}\n");
                        LogDebug($"FATAL STARTUP ERROR: {ex}");
                    }
                    return;
                }

                File.AppendAllText(bootstrapLogPath, $"[{DateTime.UtcNow:O}] Normal desktop startup sequence starting.\n");
                LogDebug("Normal desktop startup...");
                
                File.AppendAllText(bootstrapLogPath, $"[{DateTime.UtcNow:O}] Registering Native Messaging Host...\n");
                RegisterNativeMessagingHost();
                File.AppendAllText(bootstrapLogPath, $"[{DateTime.UtcNow:O}] Native Messaging Host registered.\n");
                
                File.AppendAllText(bootstrapLogPath, $"[{DateTime.UtcNow:O}] Building Avalonia App...\n");
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
                File.AppendAllText(bootstrapLogPath, $"[{DateTime.UtcNow:O}] Avalonia App Exited gracefully.\n");
            }
            catch (Exception ex)
            {
                try 
                {
                    File.AppendAllText(bootstrapLogPath, $"[{DateTime.UtcNow:O}] FATAL BOOTSTRAP ERROR: {ex}\n");
                } 
                catch { }
                throw;
            }
        }

        /// <summary>
        /// Sidequest 2: Monitors for a build lock file. Kills the process immediately if found.
        /// </summary>
        private static void InitializeKillSwitch()
        {
            string fullLockPath = Path.Combine(LockFileDir, LockFileName);

            // Prevent startup if file already exists
            if (File.Exists(fullLockPath))
            {
                LogDebug("Build lock file found on startup. Aborting execution.");
                Environment.Exit(0);
            }

            // Watch for file creation during runtime
            _killSwitchWatcher = new FileSystemWatcher(LockFileDir, LockFileName)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime,
                EnableRaisingEvents = true
            };

            _killSwitchWatcher.Created += (sender, e) =>
            {
                LogDebug("Kill switch activated by MSBuild! Shutting down gracefully for overwrite.");
                Environment.Exit(0); // Instantly frees the .exe lock for Visual Studio
            };
        }

        private static void RegisterAutostart()
        {
            try
            {
                string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exePath))
                {
                    using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                    if (key != null)
                    {
                        key.SetValue("EZKPM_Client", $"\"{exePath}\" --autostart");
                        LogDebug("Successfully registered autostart in registry.");
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Failed to register autostart: {ex.Message}");
            }
        }

        private static async Task RunNativeHostProxyAsync()
        {
            try
            {
                using var stdin = Console.OpenStandardInput();
                using var stdout = Console.OpenStandardOutput();
                var lengthBytes = new byte[4];

                while (true)
                {
                    using var cts = new CancellationTokenSource(10000); // 10 seconds timeout for inactivity? No, wait, if there's no message, it should just wait indefinitely until a message arrives.
                    // But if it starts reading length, and it's incomplete, THEN it should timeout.
                    // Let's use a 5-minute timeout for overall inactivity just to prevent zombie processes.
                    using var idleCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                    
                    int bytesRead = await stdin.ReadAsync(lengthBytes, 0, 4, idleCts.Token);
                    if (bytesRead == 0) 
                    {
                        LogNativeHost("Browser closed STDIN. Exiting.");
                        break;
                    }
                    if (bytesRead != 4)
                    {
                        LogNativeHost($"Warning: Expected 4 bytes for length, got {bytesRead}. Exiting.");
                        break;
                    }

                    int messageLength = BitConverter.ToInt32(lengthBytes, 0);
                    if (messageLength <= 0 || messageLength > 10 * 1024 * 1024)
                    {
                        LogNativeHost($"Invalid message length: {messageLength}. Exiting.");
                        break;
                    }

                    var buffer = new byte[messageLength];
                    using var readCts = new CancellationTokenSource(5000); // 5 sec timeout to read the body
                    int bodyBytesRead = await stdin.ReadAsync(buffer, 0, messageLength, readCts.Token);
                    if (bodyBytesRead != messageLength)
                    {
                        LogNativeHost($"Warning: Expected {messageLength} body bytes, got {bodyBytesRead}. Exiting.");
                        break;
                    }

                    string jsonMessage = System.Text.Encoding.UTF8.GetString(buffer);
                    LogNativeHost($"Received from browser: {jsonMessage}");

                    string responseJson = await SendToDesktopClient(jsonMessage);
                    LogNativeHost($"Sent response back to browser.");

                    byte[] responseBytes = System.Text.Encoding.UTF8.GetBytes(responseJson);
                    byte[] responseLength = BitConverter.GetBytes(responseBytes.Length);

                    await stdout.WriteAsync(responseLength, 0, responseLength.Length);
                    await stdout.WriteAsync(responseBytes, 0, responseBytes.Length);
                    await stdout.FlushAsync();
                }
            }
            catch (Exception ex)
            {
                LogNativeHost($"PROXY CRASH: {ex.Message}");
            }
        }

        private static async Task<string> SendToDesktopClient(string jsonMessage)
        {
            try
            {
                using var pipeClient = new System.IO.Pipes.NamedPipeClientStream(".", "EZKPM_BrowserBridge_Pipe", System.IO.Pipes.PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous);
                await pipeClient.ConnectAsync(2000);

                using var reader = new StreamReader(pipeClient, new System.Text.UTF8Encoding(false), leaveOpen: true);
                using var writer = new StreamWriter(pipeClient, new System.Text.UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                
                await writer.WriteLineAsync(jsonMessage);
                string response = await reader.ReadLineAsync();
                return response ?? "{\"error\": \"Empty response from desktop client\"}";
            }
            catch (Exception ex)
            {
                LogNativeHost($"Pipe Error: {ex.Message}");
                return $"{{\"error\": \"Failed to connect to vault: {ex.Message}\"}}";
            }
        }

        private static void RegisterNativeMessagingHost()
        {
            try
            {
                string manifestName = "com.ezkpm.nativehost";
                string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                string dirPath = Path.GetDirectoryName(exePath);
                string manifestPath = Path.Combine(dirPath, "com.ezkpm.nativehost.json");

                string manifest = $@"{{
  ""name"": ""{manifestName}"",
  ""description"": ""EZKPM Native Messaging Host"",
  ""path"": ""{exePath.Replace("\\", "\\\\")}"",
  ""type"": ""stdio"",
  ""allowed_origins"": [
    ""chrome-extension://codaoiocjpdmphogabicpkaiheklopgm/""
  ]
}}";
                File.WriteAllText(manifestPath, manifest, System.Text.Encoding.UTF8);

                using var chromeKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey($@"Software\Google\Chrome\NativeMessagingHosts\{manifestName}");
                chromeKey.SetValue("", manifestPath);

                using var edgeKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey($@"Software\Microsoft\Edge\NativeMessagingHosts\{manifestName}");
                edgeKey.SetValue("", manifestPath);

                // Install Extension in Browser via Registry (External Extension)
                string extensionId = "codaoiocjpdmphogabicpkaiheklopgm";
                string extensionPath = Path.Combine(dirPath, "BrowserExtension");
                
                using var chromeExtKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey($@"Software\Google\Chrome\Extensions\{extensionId}");
                chromeExtKey.SetValue("path", extensionPath);
                chromeExtKey.SetValue("version", "1.0.0");

                using var edgeExtKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey($@"Software\Microsoft\Edge\Extensions\{extensionId}");
                edgeExtKey.SetValue("path", extensionPath);
                edgeExtKey.SetValue("version", "1.0.0");

                LogDebug("Native Messaging Host and Extension registered successfully.");
            }
            catch (Exception ex)
            {
                LogDebug($"Failed to register Native Host: {ex.Message}");
            }
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont();
    }

    internal class RealDesktopVaultOrchestrator : IVaultOrchestrator
    {
        private readonly VaultApiClient _apiClient;
        private readonly VaultCryptoService _cryptoService;

        public RealDesktopVaultOrchestrator(VaultApiClient apiClient, VaultCryptoService cryptoService)
        {
            _apiClient = apiClient;
            _cryptoService = cryptoService;
        }

        public Task<dynamic> CheckAssetForUrlAsync(string url)
        {
            Program.LogDebug($"Checking asset for URL: {url}");

            // In reality, search the local metadata index or server.
            // For the first test application, return a dummy payment asset metadata block.
            dynamic result = new System.Dynamic.ExpandoObject();
            result.AssetId = Guid.NewGuid();
            result.IsPaymentAsset = true;

            return Task.FromResult<dynamic>(result);
        }

        public async Task<bool> EnforceAuditLogInteractionAsync(Guid assetId)
        {
            Program.LogDebug("EnforceAuditLogInteractionAsync invoked! Marshaling to UI thread...");
            var tcs = new TaskCompletionSource<bool>();

            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    Program.LogDebug("UI thread reached. Creating AuditDialog...");
                    var dialog = new AuditDialog();
                    Program.LogDebug("Displaying AuditDialog...");

                    var result = await dialog.ShowAuditPromptAsync();
                    Program.LogDebug($"Dialog closed. Result: IsAuthorized={result.IsAuthorized}");

                    if (result.IsAuthorized)
                    {
                        // Fetch latest hash from PDP
                        byte[] previousHash = await _apiClient.GetLatestAuditHashAsync(assetId);

                        // Generate Audit Log using CryptoService
                        string logMessage = $"Amount: {result.Amount}, Order: {result.OrderId}";
                        var auditDto = _cryptoService.CreateAuditLogRequest(logMessage, previousHash);
                        
                        // Send to PDP Server
                        bool success = await _apiClient.AppendAuditLogAsync(assetId, auditDto);
                        Program.LogDebug($"Audit Log Sync to PDP: {(success ? "Success" : "Failed")}");
                        
                        tcs.SetResult(success);
                    }
                    else
                    {
                        tcs.SetResult(false);
                    }
                }
                catch (Exception ex)
                {
                    Program.LogDebug($"UI THREAD CRASH: {ex}");
                    tcs.SetResult(false);
                }
            });

            return await tcs.Task;
        }

        public async Task<dynamic> DecryptAssetAsync(Guid assetId)
        {
            Program.LogDebug("Decrypting asset...");

            // 1. Fetch encrypted Asset from VaultController (PDP)
            var assetDto = await _apiClient.GetAssetAsync(assetId);

            if (assetDto == null)
                throw new InvalidOperationException("Asset not found or access denied.");

            if (assetDto.IsExpired)
            {
                Program.LogDebug("Asset is expired (FA 30). Denying access.");
                throw new UnauthorizedAccessException("Asset is expired.");
            }

            // 2. Perform Zero-Knowledge Decryption locally
            var decryptedData = _cryptoService.DecryptAsset(assetDto);
            
            return decryptedData;
        }
    }
}