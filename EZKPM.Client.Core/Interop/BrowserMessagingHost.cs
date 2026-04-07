using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace EZKPM.Client.Core.Interop
{
    /// <summary>
    /// Setzt die Kommunikation zwischen der Manifest V3 Browser-Extension und dem Desktop-Client um.
    /// Nutzt Native Messaging (stdin/stdout). Verhindert, dass Krypto-Schlüssel im Browser-Speicher landen.
    /// </summary>
    public class BrowserMessagingHost
    {
        private readonly Stream _stdin;
        private readonly Stream _stdout;

        // Mock-Abhängigkeiten für die Orchestrierung
        // In der echten App werden diese via Dependency Injection injiziert.
        private readonly IVaultOrchestrator _vaultOrchestrator;

        public BrowserMessagingHost(IVaultOrchestrator vaultOrchestrator)
        {
            _stdin = Console.OpenStandardInput();
            _stdout = Console.OpenStandardOutput();
            _vaultOrchestrator = vaultOrchestrator;
        }

        /// <summary>
        /// Startet den blockierenden Listener-Loop.
        /// Native Messaging nutzt ein 4-Byte Prefix für die Länge der JSON-Nachricht.
        /// </summary>
        public async Task ListenAsync()
        {
            var lengthBytes = new byte[4];

            while (true)
            {
                int bytesRead = await _stdin.ReadAsync(lengthBytes, 0, 4);
                if (bytesRead == 0) break; // Verbindung durch Browser getrennt

                int messageLength = BitConverter.ToInt32(lengthBytes, 0);
                var buffer = new byte[messageLength];
                await _stdin.ReadAsync(buffer, 0, messageLength);

                string jsonMessage = Encoding.UTF8.GetString(buffer);
                await ProcessMessageAsync(jsonMessage);
            }
        }

        private async Task ProcessMessageAsync(string jsonMessage)
        {
            try
            {
                var request = JsonSerializer.Deserialize<ExtensionRequest>(jsonMessage);

                if (request?.Type == "REQUEST_AUTOFILL")
                {
                    await HandleAutofillRequestAsync(request.Url);
                }
            }
            catch (Exception)
            {
                // Silent Fail (Anti-Reconnaissance) - Bei ungültigen Payloads nichts loggen oder zurückgeben
            }
        }

        private async Task HandleAutofillRequestAsync(string url)
        {
            // 1. Prüfen, ob für diese URL ein Asset existiert (via deterministischem URL-Hash zum Server)
            var assetMetadata = await _vaultOrchestrator.CheckAssetForUrlAsync(url);
            if (assetMetadata == null) return; // Nichts gefunden

            // 2. FA 21 & FA 22: Pflicht-Logging bei Payment-Assets
            if (assetMetadata.IsPaymentAsset)
            {
                // Wir senden einen Block-Befehl an die Extension und triggern lokal 
                // das Desktop-UI (Avalonia/MAUI), um Betrag/Bestellnummer zu erzwingen.
                SendMessage(new ExtensionResponse
                {
                    Type = "AUDIT_REQUIRED",
                    Message = "Bitte autorisieren Sie die Zahlungsdaten im Desktop-Client (FA 22)."
                });

                // Der Orchestrator blockiert hier, bis das UI das Audit-Log generiert 
                // und die Hash-Chain beim Server (PDP) erfolgreich validiert wurde!
                bool auditSuccess = await _vaultOrchestrator.EnforceAuditLogInteractionAsync(assetMetadata.AssetId);

                if (!auditSuccess)
                {
                    SendMessage(new ExtensionResponse { Type = "ACCESS_DENIED" });
                    return;
                }
            }

            // 3. Asset lokal (PEP) entschlüsseln
            var decryptedCredentials = await _vaultOrchestrator.DecryptAssetAsync(assetMetadata.AssetId);

            // 4. Nur für den Moment des Autofills an die Extension senden (Stealth-Injection folgt im Browser)
            SendMessage(new ExtensionResponse
            {
                Type = "AUTOFILL_DATA",
                Username = decryptedCredentials.Username,
                Password = decryptedCredentials.Password // Extension muss dies sofort nach Injection wipen!
            });
        }

        /// <summary>
        /// Sendet eine Nachricht zurück an die Browser-Extension.
        /// Hängt das 4-Byte Längen-Prefix korrekt an.
        /// </summary>
        private void SendMessage(ExtensionResponse response)
        {
            string jsonResponse = JsonSerializer.Serialize(response);
            byte[] bytes = Encoding.UTF8.GetBytes(jsonResponse);
            byte[] lengthPrefix = BitConverter.GetBytes(bytes.Length);

            _stdout.Write(lengthPrefix, 0, lengthPrefix.Length);
            _stdout.Write(bytes, 0, bytes.Length);
            _stdout.Flush();
        }
    }

    // --- DTOs ---
    public class ExtensionRequest
    {
        public string Type { get; set; }
        public string Url { get; set; }
    }

    public class ExtensionResponse
    {
        public string Type { get; set; }
        public string Message { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
    }

    // --- Mock Interface ---
    public interface IVaultOrchestrator
    {
        Task<dynamic> CheckAssetForUrlAsync(string url);
        Task<bool> EnforceAuditLogInteractionAsync(Guid assetId);
        Task<dynamic> DecryptAssetAsync(Guid assetId);
    }
}