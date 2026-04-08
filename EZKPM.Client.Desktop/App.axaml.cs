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
                    desktop.MainWindow = new MainWindow();
                }
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}