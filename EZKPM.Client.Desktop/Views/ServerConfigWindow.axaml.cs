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
