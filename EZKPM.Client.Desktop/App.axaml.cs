using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using System.Threading.Tasks;
using System;
using System.Linq;

namespace EZKPM.Client.Desktop
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Architektur-Weiche: Prüfen, wie die App gestartet wurde
                var args = Environment.GetCommandLineArgs();
                bool isExtensionMode = args.Any(a => a.StartsWith("chrome-extension://") || a.StartsWith("ms-browser-extension://"));

                // Nur wenn der User die App NORMAL startet, zeigen wir das Hauptfenster (Welcome to Avalonia).
                // Im Extension-Modus bleibt die App komplett unsichtbar im Hintergrund!
                if (!isExtensionMode)
                {
                    desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;

                    // Load CLI and File Config
                    Services.ConfigurationManager.LoadConfig(args);
                    
                    // Pageant Emulator is now started in MainWindow.axaml.cs

                    // Start Updater Service
                    var updaterLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<Services.UpdaterService>.Instance;
                    var updaterService = new Services.UpdaterService(updaterLogger);
                    _ = updaterService.StartAsync(new System.Threading.CancellationToken());

                    // Fire-and-forget initialization
                    _ = InitializeAppAsync(desktop);
                }
            }

            base.OnFrameworkInitializationCompleted();
        }

        private async Task InitializeAppAsync(IClassicDesktopStyleApplicationLifetime desktop)
        {
            await Task.Yield();
            
            var args = Environment.GetCommandLineArgs();
            var pairingArg = args.FirstOrDefault(a => a.StartsWith("ezkpm://pair?code=", StringComparison.OrdinalIgnoreCase));
            
            if (pairingArg != null)
            {
                var code = pairingArg.Replace("ezkpm://pair?code=", "", StringComparison.OrdinalIgnoreCase).Trim();
                var pairingWindow = new Views.PairingWindow(code);
                desktop.MainWindow = pairingWindow;
                pairingWindow.Show();
                return;
            }

            if (args.Any(a => a.Equals("--headless", StringComparison.OrdinalIgnoreCase)))
            {
                await StartHeadlessModeAsync(desktop);
                return;
            }

            var startup = new Views.StartupWindow();
            startup.Closed += (s, e) =>
            {
                if (!startup.IsAuthenticated)
                {
                    desktop.Shutdown(0);
                }
            };
            desktop.MainWindow = startup;
            
            SetupTrayIcon(desktop);

            if (!Program.IsAutoStart)
            {
                startup.Show();
            }
        }

        private async Task StartHeadlessModeAsync(IClassicDesktopStyleApplicationLifetime desktop)
        {
            try
            {
                var cryptoWrapper = new EZKPM.Client.Core.Cryptography.HybridPqcKeyWrapper();
                var cryptoService = new EZKPM.Client.Core.Cryptography.VaultCryptoService(cryptoWrapper);

                if (!EZKPM.Client.Core.Cryptography.DpapiMasterKeyStore.HasMachineSecret())
                {
                    Program.LogDebug("Headless mode failed: No DPAPI Machine Secret found. Please run the client once interactively with the service account to pair the device.");
                    desktop.Shutdown(-1);
                    return;
                }

                string legacyPwd = EZKPM.Client.Core.Security.LegacyPasswordStore.GetLegacyPassword();
                string adBlob = EZKPM.Client.Desktop.Services.AdKeyStorageService.RetrieveKeyFromAd();
                string tpmBlob = EZKPM.Client.Desktop.Services.TpmKeyStorageService.RetrieveTpmBlob();

                var result = cryptoService.InitializeFromStorage(
                    adBlob, tpmBlob, legacyPwd, 
                    EZKPM.Client.Desktop.Services.TpmKeyStorageService.IsTpmAvailable() ? EZKPM.Client.Desktop.Services.TpmKeyStorageService.ProtectHardwarePepper : null,
                    EZKPM.Client.Desktop.Services.TpmKeyStorageService.IsTpmAvailable() ? EZKPM.Client.Desktop.Services.TpmKeyStorageService.UnprotectHardwarePepper : null,
                    out _, out _, out _);

                if (result == EZKPM.Client.Core.Cryptography.VaultCryptoService.CryptoInitResult.Success)
                {
                    Program.LogDebug("Headless mode: DPAPI decryption successful.");
                    
                    // Wir erstellen das MainWindow, lassen es aber unsichtbar!
                    var main = new MainWindow(cryptoService, null);
                    desktop.MainWindow = main;
                    // Kein main.Show() aufrufen! 
                    // MainWindow.LoadAssetsAsync kümmert sich um den Start der API.
                }
                else
                {
                    Program.LogDebug($"Headless mode failed: Crypto Init Result = {result}");
                    desktop.Shutdown(-1);
                }
            }
            catch (Exception ex)
            {
                Program.LogDebug($"Headless mode fatal error: {ex.Message}");
                desktop.Shutdown(-1);
            }
        }

        private void SetupTrayIcon(IClassicDesktopStyleApplicationLifetime desktop)
        {
            try
            {
                var trayIcon = new Avalonia.Controls.TrayIcon
                {
                    Icon = new Avalonia.Controls.WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://EZKPM.Client.Desktop/Assets/icon.ico"))),
                    ToolTipText = "Ironclad Vault (EZKPM)",
                    IsVisible = true
                };

                var menu = new Avalonia.Controls.NativeMenu();
                var openItem = new Avalonia.Controls.NativeMenuItem { Header = "Tresor öffnen" };
                openItem.Click += (s, e) => ShowCurrentWindow(desktop);

                var exitItem = new Avalonia.Controls.NativeMenuItem { Header = "Beenden" };
                exitItem.Click += (s, e) => Environment.Exit(0);

                menu.Items.Add(openItem);
                menu.Items.Add(exitItem);

                trayIcon.Menu = menu;
                trayIcon.Clicked += (s, e) => ShowCurrentWindow(desktop);
            }
            catch (Exception ex)
            {
                Program.LogDebug($"Failed to create TrayIcon: {ex.Message}");
            }
        }

        private void ShowCurrentWindow(IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (desktop.MainWindow != null)
            {
                desktop.MainWindow.Show();
                desktop.MainWindow.WindowState = Avalonia.Controls.WindowState.Normal;
                desktop.MainWindow.Activate();
            }
        }
    }
}