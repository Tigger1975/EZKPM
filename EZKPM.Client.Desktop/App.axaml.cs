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
                    
                    // Fire-and-forget initialization
                    _ = InitializeAppAsync(desktop);
                }
            }

            base.OnFrameworkInitializationCompleted();
        }

        private async Task InitializeAppAsync(IClassicDesktopStyleApplicationLifetime desktop)
        {
            await Task.Yield();
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