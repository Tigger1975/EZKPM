using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using EZKPM.Client.Core.Interop;

namespace EZKPM.Client.Desktop
{
    internal class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static async Task Main(string[] args)
        {
            // 1. Weiche für Native Messaging (FA 21 / FA 22 Bridge)
            // Chrome/Edge startet den Host immer mit einem Argument, das die Extension-Origin enthält.
            if (args.Any(a => a.StartsWith("chrome-extension://") || a.StartsWith("ms-browser-extension://")))
            {
                // Headless-Modus: Kein UI laden, nur die sichere I/O-Pipe betreiben
                await RunNativeMessagingHostAsync();
                return;
            }

            // 2. Normaler Start (User hat die App via Startmenü/Doppelklick geöffnet)
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        /// <summary>
        /// Startet den Hintergrund-Listener für die Browser-Erweiterung.
        /// </summary>
        private static async Task RunNativeMessagingHostAsync()
        {
            try
            {
                // TODO: Später binden wir hier unseren echten VaultOrchestrator per DI ein.
                // Für den Moment nutzen wir einen Dummy, damit die Pipe steht.
                var orchestrator = new DummyVaultOrchestrator();
                var host = new BrowserMessagingHost(orchestrator);

                await host.ListenAsync();
            }
            catch (Exception)
            {
                // Anti-Forensik: Wenn die Pipe crasht, lautlos beenden. Keine Logs ins EventLog schreiben!
                Environment.Exit(1);
            }
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }

    /// <summary>
    /// Temporärer Dummy-Orchestrator, bis das UI und die Krypto-Engine final verknüpft sind.
    /// </summary>
    internal class DummyVaultOrchestrator : IVaultOrchestrator
    {
        public Task<dynamic> CheckAssetForUrlAsync(string url)
        {
            // Simuliert einen Treffer für die aktuelle URL (Testdaten)
            return Task.FromResult<dynamic>(new { AssetId = Guid.NewGuid(), IsPaymentAsset = true });
        }

        public Task<bool> EnforceAuditLogInteractionAsync(Guid assetId)
        {
            // Hier würde sich später das Avalonia-Fenster für das Pflicht-Log öffnen!
            // Da wir noch kein UI haben, simulieren wir vorerst einen Fehlschlag/Block.
            return Task.FromResult(false);
        }

        public Task<dynamic> DecryptAssetAsync(Guid assetId)
        {
            return Task.FromResult<dynamic>(new { Username = "testuser", Password = "testpassword" });
        }
    }
}