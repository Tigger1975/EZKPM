using Avalonia.Controls;
using Avalonia.Interactivity;
using EZKPM.Client.Core.Cryptography;
using System;
using System.Threading.Tasks;

namespace EZKPM.Client.Desktop.Views
{
    public partial class StartupWindow : Window
    {
        public bool IsAuthenticated { get; private set; } = false;
        public VaultCryptoService CryptoService { get; private set; }

        public StartupWindow()
        {
            Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
            CryptoService = new VaultCryptoService(new HybridPqcKeyWrapper());

            // Initialize FA 14 Session Manager once
            Services.SessionManager.Initialize();

            // Start the sequence
            _ = RunStartupSequenceAsync();
        }

        private async Task RunStartupSequenceAsync()
        {
            // 1. Config Check Phase
            bool configOk = !string.IsNullOrEmpty(Services.ConfigurationManager.CurrentConfig.ServerUrl) && 
                            await Services.ConfigurationManager.IsServerReachableAsync(Services.ConfigurationManager.CurrentConfig.ServerUrl);

            if (!configOk)
            {
                var configPanel = this.FindControl<StackPanel>("ServerConfigPanel");
                var urlBox = this.FindControl<TextBox>("UrlTextBox");
                if (configPanel != null) configPanel.IsVisible = true;
                if (urlBox != null) urlBox.Text = Services.ConfigurationManager.CurrentConfig.ServerUrl;
                return; // Wait for SaveConfigButton_Click
            }

            // If config is OK, proceed to Auth phase
            ShowAuthPhase();
        }

        private async void SaveConfigButton_Click(object sender, RoutedEventArgs e)
        {
            var urlTextBox = this.FindControl<TextBox>("UrlTextBox");
            var statusText = this.FindControl<TextBlock>("ConfigStatusText");
            var testProgress = this.FindControl<ProgressBar>("ConfigTestProgress");
            var saveBtn = this.FindControl<Button>("SaveConfigButton");

            if (urlTextBox == null || statusText == null || testProgress == null || saveBtn == null) return;

            string input = urlTextBox.Text;
            if (string.IsNullOrWhiteSpace(input))
            {
                statusText.Text = EZKPM.Client.Desktop.Resources.AppStrings.Startup_ErrNoUrl;
                statusText.IsVisible = true;
                return;
            }

            string url = Services.ConfigurationManager.EnsureValidUrl(input);

            saveBtn.IsEnabled = false;
            testProgress.IsVisible = true;
            statusText.IsVisible = false;

            bool isReachable = await Services.ConfigurationManager.IsServerReachableAsync(url);

            testProgress.IsVisible = false;
            saveBtn.IsEnabled = true;

            if (isReachable)
            {
                Services.ConfigurationManager.CurrentConfig.ServerUrl = url;
                Services.ConfigurationManager.SaveConfig();
                
                var configPanel = this.FindControl<StackPanel>("ServerConfigPanel");
                if (configPanel != null) configPanel.IsVisible = false;

                ShowAuthPhase();
            }
            else
            {
                statusText.Text = EZKPM.Client.Desktop.Resources.AppStrings.Startup_ErrServerUnreachable;
                statusText.IsVisible = true;
            }
        }

        private void ShowAuthPhase()
        {
            var authPanel = this.FindControl<StackPanel>("AuthPanel");
            if (authPanel != null) authPanel.IsVisible = true;

            if (DpapiMasterKeyStore.HasMachineSecret())
            {
                UnlockButton_Click(this, new RoutedEventArgs());
            }
            else
            {
                var unlockBtn = this.FindControl<Button>("UnlockButton");
                var status = this.FindControl<TextBlock>("StatusText");
                var setupPanel = this.FindControl<StackPanel>("SetupPanel");
                if (unlockBtn != null) unlockBtn.IsVisible = false;
                if (status != null) 
                {
                    status.Text = EZKPM.Client.Desktop.Resources.AppStrings.Startup_DeviceNotPaired;
                    status.Foreground = Avalonia.Media.Brushes.Yellow;
                    status.IsVisible = true;
                }
                if (setupPanel != null) setupPanel.IsVisible = true;
            }
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
                status.Text = EZKPM.Client.Desktop.Resources.AppStrings.Startup_DecryptingSso;
                status.Foreground = Avalonia.Media.Brushes.Orange;
                status.IsVisible = true;
            }

            await Task.Delay(100);

            try
            {
                string legacyPwd = EZKPM.Client.Core.Security.LegacyPasswordStore.GetLegacyPassword();
                bool isCorrect = CryptoService.Initialize(legacyPwd);
                
                if (isCorrect)
                {
                    if (Services.SessionManager.RequiresStartupAuth && Services.SessionManager.IsLocked)
                    {
                        bool success = Services.SessionManager.EnsureAuthenticated(EZKPM.Client.Desktop.Resources.AppStrings.Startup_SecurityLock);
                        if (!success)
                        {
                            if (status != null)
                            {
                                status.Text = EZKPM.Client.Desktop.Resources.AppStrings.Startup_AuthCanceled;
                                status.Foreground = Avalonia.Media.Brushes.Red;
                                status.IsVisible = true;
                            }
                            if (prog != null) prog.IsVisible = false;
                            if (unlockBtn != null) unlockBtn.IsVisible = true;
                            return;
                        }
                    }

                    ProceedToSplashPhase();
                }
                else
                {
                    if (unlockBtn != null) unlockBtn.IsVisible = false;
                    if (prog != null) prog.IsVisible = false;
                    if (status != null) status.IsVisible = false;
                    
                    var migrationPanel = this.FindControl<StackPanel>("MigrationPanel");
                    if (migrationPanel != null) migrationPanel.IsVisible = true;
                }
            }
            catch (RequiresRecoveryException ex)
            {
                if (unlockBtn != null) unlockBtn.IsVisible = false;
                if (prog != null) prog.IsVisible = false;
                if (status != null) status.IsVisible = false;
                if (recPanel != null) recPanel.IsVisible = true;
            }
            catch (Exception ex)
            {
                if (status != null)
                {
                    status.Text = string.Format(EZKPM.Client.Desktop.Resources.AppStrings.Startup_CriticalError, ex.Message);
                    status.Foreground = Avalonia.Media.Brushes.Red;
                    status.IsVisible = true;
                }
                if (prog != null) prog.IsVisible = false;
                if (unlockBtn != null) unlockBtn.IsVisible = true;
            }
        }

        private void ProceedToSplashPhase()
        {
            var authPanel = this.FindControl<StackPanel>("AuthPanel");
            var splashPanel = this.FindControl<StackPanel>("SplashPanel");
            
            if (authPanel != null) authPanel.IsVisible = false;
            if (splashPanel != null) splashPanel.IsVisible = true;

            IsAuthenticated = true;
            
            // Allow the window to stay open for MainWindow to take over
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Instantiate MainWindow in the background. MainWindow takes this window as Splash reference.
                var main = new MainWindow(CryptoService, this);
                
                // MainWindow handles the `this.Close()` internally once it's fully loaded,
                // or we can invoke it directly if MainWindow calls it.
            }
        }

        private void SetupDeviceButton_Click(object sender, RoutedEventArgs e)
        {
            var keyBox = this.FindControl<TextBox>("SecretKeyTextBox");
            var status = this.FindControl<TextBlock>("StatusText");
            try
            {
                byte[] parsed = SecretKeyService.ParseSecretKey(keyBox?.Text ?? "");
                DpapiMasterKeyStore.SaveMachineSecret(parsed);
                
                var setupPanel = this.FindControl<StackPanel>("SetupPanel");
                if (setupPanel != null) setupPanel.IsVisible = false;
                
                UnlockButton_Click(sender, e);
            }
            catch (Exception ex)
            {
                if (status != null)
                {
                    status.Text = string.Format(EZKPM.Client.Desktop.Resources.AppStrings.Startup_Error, ex.Message);
                    status.Foreground = Avalonia.Media.Brushes.Red;
                    status.IsVisible = true;
                }
            }
        }

        private void CreateVaultButton_Click(object sender, RoutedEventArgs e)
        {
            string newKey = SecretKeyService.GenerateSecretKey();
            byte[] parsed = SecretKeyService.ParseSecretKey(newKey);
            DpapiMasterKeyStore.SaveMachineSecret(parsed);

            var setupPanel = this.FindControl<StackPanel>("SetupPanel");
            var status = this.FindControl<TextBlock>("StatusText");
            if (setupPanel != null) setupPanel.IsVisible = false;

            if (status != null)
            {
                status.Text = string.Format(EZKPM.Client.Desktop.Resources.AppStrings.Startup_RecoveryKey, newKey);
                status.Foreground = Avalonia.Media.Brushes.Yellow;
                status.IsVisible = true;
            }

            Avalonia.Threading.DispatcherTimer.RunOnce(() =>
            {
                UnlockButton_Click(sender, e);
            }, TimeSpan.FromSeconds(8));
        }

        private void MigrateButton_Click(object sender, RoutedEventArgs e)
        {
            var txtBox = this.FindControl<TextBox>("LegacyPasswordTextBox");
            var status = this.FindControl<TextBlock>("StatusText");
            string pwd = txtBox?.Text ?? "";
            
            bool isCorrect = CryptoService.Initialize(pwd);
            if (isCorrect)
            {
                EZKPM.Client.Core.Security.LegacyPasswordStore.SaveLegacyPassword(pwd);
                ProceedToSplashPhase();
            }
            else
            {
                if (status != null)
                {
                    status.Text = EZKPM.Client.Desktop.Resources.AppStrings.Startup_WrongLegacyPwd;
                    status.Foreground = Avalonia.Media.Brushes.Red;
                    status.IsVisible = true;
                }
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
                status.Text = EZKPM.Client.Desktop.Resources.AppStrings.Startup_RecoveryRequested;
                status.Foreground = Avalonia.Media.Brushes.Orange;
                status.IsVisible = true;
            }
            if (prog != null) prog.IsVisible = true;

            try
            {
                var sid = System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;
                string ephemeralPubKey = "mock-ephemeral-pub-key-base64";

                var handler = new System.Net.Http.HttpClientHandler { UseDefaultCredentials = true };
                var client = new EZKPM.Client.Core.Services.VaultApiClient(new System.Net.Http.HttpClient(handler) { BaseAddress = new Uri(EZKPM.Client.Desktop.Services.ConfigurationManager.CurrentConfig.ServerUrl) });
                
                using var sha256 = System.Security.Cryptography.SHA256.Create();
                var hashedSid = Convert.ToBase64String(sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(sid)));
                
                await client.RequestRecoveryAsync(new EZKPM.Shared.Contracts.InitiateRecoveryRequestDto
                {
                    HashedSid = hashedSid,
                    EphemeralUserPubKey = ephemeralPubKey
                });

                if (status != null) status.Text = EZKPM.Client.Desktop.Resources.AppStrings.Startup_RecoveryWaitAdmin;
                var simBtn = this.FindControl<Button>("SimulateAdminButton");
                if (simBtn != null) simBtn.IsVisible = true;

                while (true)
                {
                    await Task.Delay(3000);
                    var statusResp = await client.GetRecoveryStatusAsync(sid);
                    
                    if (statusResp != null && statusResp.IsCompleted)
                    {
                        if (status != null)
                        {
                            status.Text = EZKPM.Client.Desktop.Resources.AppStrings.Startup_RecoverySuccess;
                            status.Foreground = Avalonia.Media.Brushes.Green;
                        }
                        
                        await Task.Delay(1000);

                        byte[] recoveredKey = new byte[64]; // Mock
                        DpapiMasterKeyStore.SaveMachineSecret(recoveredKey);

                        ProceedToSplashPhase();
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                if (status != null)
                {
                    status.Text = string.Format(EZKPM.Client.Desktop.Resources.AppStrings.Startup_Error, ex.Message);
                    status.Foreground = Avalonia.Media.Brushes.Red;
                }
            }
        }

        private async void SimulateAdminButton_Click(object sender, RoutedEventArgs e)
        {
            var btn = this.FindControl<Button>("SimulateAdminButton");
            if (btn != null) btn.IsEnabled = false;

            var handler = new System.Net.Http.HttpClientHandler { UseDefaultCredentials = true };
            var client = new EZKPM.Client.Core.Services.VaultApiClient(new System.Net.Http.HttpClient(handler) { BaseAddress = new Uri(EZKPM.Client.Desktop.Services.ConfigurationManager.CurrentConfig.ServerUrl) });
            var sid = System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;
            var statusResp = await client.GetRecoveryStatusAsync(sid);

            if (statusResp != null)
            {
                await client.ApproveRecoveryAsync(new EZKPM.Shared.Contracts.ProvideRecoveryShareDto
                {
                    RecoveryRequestId = statusResp.RecoveryRequestId,
                    AdminHashedSid = "HASHED-ADMIN-1",
                    EncryptedShareBlob = "mock-share-1"
                });

                await client.ApproveRecoveryAsync(new EZKPM.Shared.Contracts.ProvideRecoveryShareDto
                {
                    RecoveryRequestId = statusResp.RecoveryRequestId,
                    AdminHashedSid = "HASHED-ADMIN-2",
                    EncryptedShareBlob = "mock-share-2"
                });
            }
        }

        public void UpdateStatus(string message, double progressPercent)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var text = this.FindControl<TextBlock>("SplashStatusText");
                if (text != null)
                {
                    text.Text = message;
                }
            });
        }
    }
}
