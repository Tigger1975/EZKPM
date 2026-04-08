using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using EZKPM.Client.Core.Interop;
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
        public static async Task Main(string[] args)
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
                var orchestrator = new RealDesktopVaultOrchestrator();
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
        public Task<dynamic> CheckAssetForUrlAsync(string url)
        {
            Program.LogDebug($"Checking asset for URL: {url}");

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

                    tcs.SetResult(result.IsAuthorized);
                }
                catch (Exception ex)
                {
                    Program.LogDebug($"UI THREAD CRASH: {ex}");
                    tcs.SetResult(false);
                }
            });

            return await tcs.Task;
        }

        public Task<dynamic> DecryptAssetAsync(Guid assetId)
        {
            Program.LogDebug("Decrypting asset...");

            dynamic result = new System.Dynamic.ExpandoObject();
            result.Username = "MyCardName";
            result.Password = "1234-5678-9012-3456";

            return Task.FromResult<dynamic>(result);
        }
    }
}