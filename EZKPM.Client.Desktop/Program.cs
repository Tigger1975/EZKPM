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
        private static string LogFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "ezkpm_debug.txt");
        private static readonly string LockFileName = "ezkpm_build.lock";
        private static readonly string LockFileDir = Path.GetTempPath();
        private static FileSystemWatcher _killSwitchWatcher;

        public static void LogDebug(string message)
        {
            try { File.AppendAllText(LogFilePath, $"[{DateTime.UtcNow:O}] {message}\n"); } catch { }
        }

        [STAThread]
        public static void Main(string[] args)
        {
            InitializeKillSwitch();

            LogDebug($"App started. Arguments: {string.Join(" ", args)}");

            if (args.Any(a => a.StartsWith("chrome-extension://") || a.StartsWith("ms-browser-extension://")))
            {
                LogDebug("Extension mode detected. Starting Avalonia headless...");
                try
                {
                    var builder = BuildAvaloniaApp();
                    var lifetime = new Avalonia.Controls.ApplicationLifetimes.ClassicDesktopStyleApplicationLifetime
                    {
                        ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown
                    };
                    builder.SetupWithLifetime(lifetime);

                    LogDebug("Avalonia initialized. Starting Native Messaging listener...");
                    _ = Task.Run(async () => await RunNativeMessagingHostAsync(lifetime));

                    lifetime.Start(args);
                }
                catch (Exception ex)
                {
                    LogDebug($"FATAL STARTUP ERROR: {ex}");
                }
                return;
            }

            LogDebug("Normal desktop startup...");
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
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

        private static async Task RunNativeMessagingHostAsync(Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime lifetime)
        {
            try
            {
                var httpClient = new HttpClient { BaseAddress = new Uri("http://localhost:5117") };
                var apiClient = new VaultApiClient(httpClient);
                var keyWrapper = new HybridPqcKeyWrapper();
                var cryptoService = new VaultCryptoService(keyWrapper);
                var orchestrator = new RealDesktopVaultOrchestrator(apiClient, cryptoService);
                var host = new BrowserMessagingHost(orchestrator);

                LogDebug("Awaiting messages from browser...");
                await host.ListenAsync();
                LogDebug("ListenAsync() terminated naturally (Browser disconnected).");
            }
            catch (Exception ex)
            {
                LogDebug($"LISTENER CRASH: {ex}");
                Environment.Exit(1);
            }
            finally
            {
                Dispatcher.UIThread.Post(() => lifetime.Shutdown());
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
                        // Generate Audit Log using CryptoService
                        string logMessage = $"Amount: {result.Amount}, Order: {result.OrderId}";
                        var auditDto = _cryptoService.CreateAuditLogRequest(logMessage);
                        
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