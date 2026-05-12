using System;
using System.Net.Http;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System.Text.Json;
using System.Security.Cryptography;

namespace EZKPM.Client.Desktop.Views
{
    public partial class PairingWindow : Window
    {
        private readonly string _pairingCode;

        public PairingWindow()
        {
            InitializeComponent();
            AttachHandlers();
        }

        public PairingWindow(string pairingCode)
        {
            InitializeComponent();
            _pairingCode = pairingCode;
            
            var codeTextBox = this.FindControl<TextBox>("CodeTextBox");
            if (codeTextBox != null && !string.IsNullOrEmpty(_pairingCode))
                codeTextBox.Text = _pairingCode;

            AttachHandlers();
        }

        private void AttachHandlers()
        {
            var registerBtn = this.FindControl<Button>("RegisterButton");
            if (registerBtn != null)
                registerBtn.Click += OnRegisterClick;

            var cancelBtn = this.FindControl<Button>("CancelButton");
            if (cancelBtn != null)
                cancelBtn.Click += (s, e) => { Environment.Exit(0); };
        }

        private async void OnRegisterClick(object sender, RoutedEventArgs e)
        {
            var registerBtn = this.FindControl<Button>("RegisterButton");
            var statusText = this.FindControl<TextBlock>("StatusText");
            var progress = this.FindControl<ProgressBar>("ProgressIndicator");

            registerBtn.IsEnabled = false;
            progress.IsVisible = true;
            statusText.Text = "Status: Verbinde mit Server...";
            statusText.Foreground = Avalonia.Media.Brushes.Yellow;

            try
            {
                // 1. Get current Windows SID
                string sid = "S-1-5-21-DUMMY-FALLBACK";
                if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                {
                    sid = WindowsIdentity.GetCurrent().User?.Value ?? sid;
                }

                // 2. Hash SID using the exact same logic as Server
                using var sha256 = SHA256.Create();
                var sidBytes = Encoding.UTF8.GetBytes(sid);
                var hashedSid = Convert.ToBase64String(sha256.ComputeHash(sidBytes));

                var pwdBox = this.FindControl<TextBox>("PasswordTextBox");
                string pwd = pwdBox?.Text ?? "";

                // 3. Generate Identity Public Key & Vault Keys
                // 3. Generate Identity Public Key & Vault Keys
                var cryptoService = new EZKPM.Client.Core.Cryptography.VaultCryptoService(new EZKPM.Client.Core.Cryptography.HybridPqcKeyWrapper());
                
                cryptoService.GenerateAndStoreNewKeys(
                    pwd, 
                    EZKPM.Client.Desktop.Services.TpmKeyStorageService.IsTpmAvailable() ? EZKPM.Client.Desktop.Services.TpmKeyStorageService.ProtectHardwarePepper : null,
                    out string newAdBlob, 
                    out string newTpmBlob, 
                    out string pubKeyBase64);

                // 4. Send to Server
                var codeBox = this.FindControl<TextBox>("CodeTextBox");
                string actualCode = codeBox?.Text ?? _pairingCode;

                var payload = new
                {
                    PairingCode = actualCode,
                    HashedSid = hashedSid,
                    IdentityPublicKey = pubKeyBase64
                };

                string json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var handler = new HttpClientHandler { UseDefaultCredentials = true };
                using var http = new HttpClient(handler) { BaseAddress = new Uri(EZKPM.Client.Desktop.Services.ConfigurationManager.CurrentConfig.ServerUrl) };
                var response = await http.PostAsync("/api/v1/auth/register-device", content);

                if (response.IsSuccessStatusCode)
                {
                    // Backup to Storage ONLY after server confirmed successful registration
                    if (!string.IsNullOrEmpty(newAdBlob))
                        EZKPM.Client.Desktop.Services.AdKeyStorageService.StoreKeyInAd(newAdBlob);
                        
                    if (!string.IsNullOrEmpty(newTpmBlob))
                        EZKPM.Client.Desktop.Services.TpmKeyStorageService.StoreTpmBlob(newTpmBlob);

                    statusText.Text = "Status: Pairing erfolgreich! Tresor wird vorbereitet.";
                    statusText.Foreground = Avalonia.Media.Brushes.LightGreen;
                    
                    // Wait a bit, then proceed to normal startup
                    await Task.Delay(2000);
                    
                    Dispatcher.UIThread.Post(() =>
                    {
                        // Bypass the >5min inactivity lock for the very first startup right after pairing
                        // because we want a smooth onboarding experience.
                        typeof(EZKPM.Client.Desktop.Services.SessionManager)
                            .GetProperty("IsLocked")?
                            .SetValue(null, false);

                        var startup = new StartupWindow();
                        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                        {
                            desktop.MainWindow = startup;
                        }
                        startup.Show();
                        this.Close();
                    });
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    statusText.Text = $"Fehler: {error}";
                    statusText.Foreground = Avalonia.Media.Brushes.Red;
                    registerBtn.IsEnabled = true;
                    progress.IsVisible = false;
                }
            }
            catch (Exception ex)
            {
                statusText.Text = $"Netzwerkfehler: {ex.Message}";
                statusText.Foreground = Avalonia.Media.Brushes.Red;
                registerBtn.IsEnabled = true;
                progress.IsVisible = false;
            }
        }
    }
}
