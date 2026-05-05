using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
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

                    var login = new Views.LoginWindow();
                    login.Closed += (s, e) =>
                    {
                        if (login.IsAuthenticated)
                        {
                            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnLastWindowClose;
                            var splash = new Views.SplashScreenWindow();
                            desktop.MainWindow = splash;
                            splash.Show();
                            
                            // MainWindow starts loading, will swap itself and close splash when ready
                            var main = new MainWindow(splash);
                        }
                        else
                        {
                            desktop.Shutdown(0);
                        }
                    };
                    desktop.MainWindow = login;
                }
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}