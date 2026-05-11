using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using EZKPM.Client.Desktop.Services;

namespace EZKPM.Client.Desktop.Views
{
    public partial class ServerConfigWindow : Window
    {
        public bool IsConfirmed { get; private set; }
        public bool IsClosed { get; private set; }
        public string SelectedUrl { get; private set; }

        public ServerConfigWindow(string currentUrl = "")
        {
            Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
            var urlTextBox = this.FindControl<TextBox>("UrlTextBox");
            if (urlTextBox != null)
            {
                urlTextBox.Text = currentUrl;
            }

            var versionText = this.FindControl<TextBlock>("VersionText");
            if (versionText != null)
            {
                versionText.Text = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";
            }

            this.Closed += (s, e) => IsClosed = true;

            // Initialize CheckBox state from Registry
            var autostartBox = this.FindControl<CheckBox>("AutostartCheckBox");
            if (autostartBox != null)
            {
                autostartBox.IsChecked = IsAutostartEnabled();
            }
        }

        private bool IsAutostartEnabled()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
                if (key != null)
                {
                    var val = key.GetValue("EZKPM_Client") as string;
                    return val != null && val.Contains("--autostart");
                }
            }
            catch { }
            return false;
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var urlTextBox = this.FindControl<TextBox>("UrlTextBox");
            var statusText = this.FindControl<TextBlock>("StatusText");
            var testProgress = this.FindControl<ProgressBar>("TestProgress");
            var saveBtn = this.FindControl<Button>("SaveButton");

            if (urlTextBox == null || statusText == null || testProgress == null || saveBtn == null) return;

            string input = urlTextBox.Text;
            if (string.IsNullOrWhiteSpace(input))
            {
                statusText.Text = "Bitte eine URL eingeben.";
                statusText.IsVisible = true;
                return;
            }

            string url = ConfigurationManager.EnsureValidUrl(input);

            saveBtn.IsEnabled = false;
            testProgress.IsVisible = true;
            statusText.IsVisible = false;

            bool isReachable = await ConfigurationManager.IsServerReachableAsync(url);

            testProgress.IsVisible = false;
            saveBtn.IsEnabled = true;

            if (isReachable)
            {
                SelectedUrl = url;
                IsConfirmed = true;

                // Handle Autostart Setting
                var autostartBox = this.FindControl<CheckBox>("AutostartCheckBox");
                if (autostartBox != null && autostartBox.IsChecked.HasValue)
                {
                    SetAutostartEnabled(autostartBox.IsChecked.Value);
                }

                this.Close(true);
            }
            else
            {
                statusText.Text = "Server nicht erreichbar. Bitte URL prüfen.";
                statusText.IsVisible = true;
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close(false);
        }

        private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var statusText = this.FindControl<TextBlock>("StatusText");
            if (btn != null) btn.IsEnabled = false;

            try
            {
                var currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";
                var url = $"{ConfigurationManager.CurrentConfig.ServerUrl}/api/updater/check?currentVersion={currentVersion}";
                
                using var client = new System.Net.Http.HttpClient();
                var response = await client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var updateInfo = System.Text.Json.JsonSerializer.Deserialize<EZKPM.Shared.Contracts.UpdateCheckResponseDto>(json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    
                    if (updateInfo != null && updateInfo.UpdateAvailable)
                    {
                        var dialog = new ConfirmationDialog($"Ein Update auf Version {updateInfo.LatestVersion} ist verfügbar.\n\nRelease Notes:\n{updateInfo.ReleaseNotes}\n\nMöchten Sie das Update jetzt herunterladen und installieren?");
                        if (await dialog.ShowDialogAsync(this))
                        {
                            await PerformManualUpdateAsync(updateInfo.DownloadUrl);
                        }
                    }
                    else
                    {
                        var dialog = new ConfirmationDialog("Sie verwenden bereits die aktuellste Version.");
                        await dialog.ShowDialogAsync(this);
                    }
                }
                else
                {
                    if (statusText != null)
                    {
                        statusText.Text = "Update-Server nicht erreichbar.";
                        statusText.IsVisible = true;
                    }
                }
            }
            catch (Exception ex)
            {
                if (statusText != null)
                {
                    statusText.Text = $"Fehler bei Prüfung: {ex.Message}";
                    statusText.IsVisible = true;
                }
            }
            finally
            {
                if (btn != null) btn.IsEnabled = true;
            }
        }

        private async System.Threading.Tasks.Task PerformManualUpdateAsync(string downloadUrl)
        {
            try
            {
                if (downloadUrl.StartsWith("/"))
                {
                    downloadUrl = ConfigurationManager.CurrentConfig.ServerUrl + downloadUrl;
                }

                var tempZipPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "EZKPM_Update.zip");
                using var client = new System.Net.Http.HttpClient();
                var response = await client.GetAsync(downloadUrl);
                response.EnsureSuccessStatusCode();

                using (var fs = new System.IO.FileStream(tempZipPath, System.IO.FileMode.Create, System.IO.FileAccess.Write))
                {
                    await response.Content.CopyToAsync(fs);
                }

                var currentDir = AppContext.BaseDirectory;
                var currentExe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(currentExe)) currentExe = System.IO.Path.Combine(currentDir, "EZKPM.Client.Desktop.exe");

                var scriptPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "EZKPM_Updater.ps1");
                var scriptContent = $@"
param()
Start-Sleep -Seconds 3
Expand-Archive -Path '{tempZipPath}' -DestinationPath '{currentDir}' -Force
Remove-Item -Path '{tempZipPath}' -Force
Start-Process -FilePath '{currentExe}'
";
                System.IO.File.WriteAllText(scriptPath, scriptContent);

                var psInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
                    UseShellExecute = true,
                    CreateNoWindow = true
                };
                System.Diagnostics.Process.Start(psInfo);

                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                var dialog = new ConfirmationDialog($"Fehler beim Herunterladen des Updates: {ex.Message}");
                await dialog.ShowDialogAsync(this);
            }
        }

        private void SetAutostartEnabled(bool enable)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (key != null)
                {
                    if (enable)
                    {
                        string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                        if (!string.IsNullOrEmpty(exePath))
                        {
                            key.SetValue("EZKPM_Client", $"\"{exePath}\" --autostart");
                        }
                    }
                    else
                    {
                        key.DeleteValue("EZKPM_Client", false);
                    }
                }
            }
            catch (Exception ex)
            {
                Program.LogDebug($"Failed to update autostart setting: {ex.Message}");
            }
        }
    }
}
