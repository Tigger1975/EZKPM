using Avalonia.Controls;
using Avalonia.Interactivity;
using EZKPM.Client.Core.Cryptography;
using System;
using System.Threading.Tasks;

namespace EZKPM.Client.Desktop.Views
{
    public partial class LoginWindow : Window
    {
        public bool IsAuthenticated { get; private set; } = false;

        public LoginWindow()
        {
            Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
        }

        private async void UnlockButton_Click(object sender, RoutedEventArgs e)
        {
            var unlockBtn = this.FindControl<Button>("UnlockButton");
            var prog = this.FindControl<ProgressBar>("UnlockProgress");
            var status = this.FindControl<TextBlock>("StatusText");
            var recPanel = this.FindControl<StackPanel>("RecoveryPanel");

            if (unlockBtn != null) unlockBtn.IsVisible = false;
            if (prog != null) prog.IsVisible = true;
            if (status != null) 
            {
                status.Text = "Entschlüssele Master-Key via DPAPI...";
                status.IsVisible = true;
            }

            await Task.Delay(500);

            try
            {
                // This will throw if DPAPI fails
                byte[] key = DpapiMasterKeyStore.GetOrGenerateMasterKey();
                
                IsAuthenticated = true;
                this.Close(true);
            }
            catch (RequiresRecoveryException)
            {
                if (prog != null) prog.IsVisible = false;
                if (status != null) status.IsVisible = false;
                if (recPanel != null) recPanel.IsVisible = true;
            }
        }

        private async void RecoveryButton_Click(object sender, RoutedEventArgs e)
        {
            var status = this.FindControl<TextBlock>("StatusText");
            var recBtn = this.FindControl<Button>("RecoveryButton");
            var prog = this.FindControl<ProgressBar>("UnlockProgress");

            if (recBtn != null) recBtn.IsEnabled = false;
            if (status != null)
            {
                status.Text = "Recovery angefordert. Generiere temporäres Schlüsselpaar...";
                status.Foreground = Avalonia.Media.Brushes.Orange;
                status.IsVisible = true;
            }
            if (prog != null) prog.IsVisible = true;

            try
            {
                var sid = System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;
                // For PoC: We just use a mock public key string. In production, this would be an actual Curve25519 PubKey
                string ephemeralPubKey = "mock-ephemeral-pub-key-base64";

                var client = new EZKPM.Client.Core.Services.VaultApiClient(new System.Net.Http.HttpClient { BaseAddress = new Uri("http://localhost:5246") });
                
                await client.RequestRecoveryAsync(new EZKPM.Shared.Contracts.InitiateRecoveryRequestDto
                {
                    AdSid = sid,
                    EphemeralUserPubKey = ephemeralPubKey
                });

                if (status != null) status.Text = "Warte auf Admin-Freigaben (Shamir's Secret Sharing)...";
                var simBtn = this.FindControl<Button>("SimulateAdminButton");
                if (simBtn != null) simBtn.IsVisible = true;

                // Polling loop
                while (true)
                {
                    await Task.Delay(3000);
                    var statusResp = await client.GetRecoveryStatusAsync(sid);
                    
                    if (statusResp != null && statusResp.IsCompleted)
                    {
                        if (status != null)
                        {
                            status.Text = "Recovery erfolgreich! Stelle Master-Key her...";
                            status.Foreground = Avalonia.Media.Brushes.Green;
                        }
                        
                        // Wait briefly for UI update
                        await Task.Delay(1000);

                        // 1. In a real scenario we decrypt the `statusResp.EncryptedShareBlobs` with the ephemeral Private Key
                        // 2. We use ShamirSecretSharing to reconstruct the Recovery Private Key
                        // 3. We decrypt the `statusResp.EncryptedMasterKeyBackup` with the Recovery Private Key
                        
                        // For PoC: We simulate the recovered key (e.g. generate a fresh one or assume standard mock)
                        byte[] recoveredKey = new byte[64]; // Mock
                        
                        // We restore DPAPI with the recovered key
                        DpapiMasterKeyStore.SaveMasterKey(recoveredKey);

                        IsAuthenticated = true;
                        this.Close(true);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                if (status != null)
                {
                    status.Text = "Fehler: " + ex.Message;
                    status.Foreground = Avalonia.Media.Brushes.Red;
                }
            }
        }

        private async void SimulateAdminButton_Click(object sender, RoutedEventArgs e)
        {
            var btn = this.FindControl<Button>("SimulateAdminButton");
            if (btn != null) btn.IsEnabled = false;

            var client = new EZKPM.Client.Core.Services.VaultApiClient(new System.Net.Http.HttpClient { BaseAddress = new Uri("http://localhost:5246") });
            var sid = System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;
            var statusResp = await client.GetRecoveryStatusAsync(sid);

            if (statusResp != null)
            {
                // Simulate Admin 1
                await client.ApproveRecoveryAsync(new EZKPM.Shared.Contracts.ProvideRecoveryShareDto
                {
                    RecoveryRequestId = statusResp.RecoveryRequestId,
                    AdminSid = "S-1-5-21-ADMIN-1",
                    EncryptedShareBlob = "mock-share-1"
                });

                // Simulate Admin 2
                await client.ApproveRecoveryAsync(new EZKPM.Shared.Contracts.ProvideRecoveryShareDto
                {
                    RecoveryRequestId = statusResp.RecoveryRequestId,
                    AdminSid = "S-1-5-21-ADMIN-2",
                    EncryptedShareBlob = "mock-share-2"
                });
            }
        }
    }
}
