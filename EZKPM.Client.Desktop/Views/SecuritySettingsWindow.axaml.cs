using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input.Platform;
using System.Threading.Tasks;
using EZKPM.Client.Core.Cryptography;
using System;

namespace EZKPM.Client.Desktop.Views
{
    public partial class SecuritySettingsWindow : Window
    {
        public SecuritySettingsWindow()
        {
            Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
            LoadSecretKey();
        }

        private void LoadSecretKey()
        {
            try
            {
                byte[] rawKey = DpapiMasterKeyStore.GetMachineSecret();
                if (rawKey != null && rawKey.Length == 16)
                {
                    string formatted = SecretKeyService.FormatSecretKey(rawKey);
                    var txt = this.FindControl<TextBox>("SecretKeyTextBox");
                    if (txt != null)
                    {
                        txt.Text = formatted;
                    }
                }
            }
            catch (Exception)
            {
                // Fallback, should not happen if authenticated
            }
        }

        private async void CopyKeyButton_Click(object sender, RoutedEventArgs e)
        {
            if (!Services.SessionManager.EnsureAuthenticated("Recovery-Schlüssel kopieren")) return;

            var txt = this.FindControl<TextBox>("SecretKeyTextBox");
            if (txt != null && !string.IsNullOrEmpty(txt.Text))
            {
                var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard != null)
                {
                    await clipboard.SetTextAsync(txt.Text);
                }
            }
        }
    }
}
