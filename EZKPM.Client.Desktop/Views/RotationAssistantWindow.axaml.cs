using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using EZKPM.Shared.Contracts;
using EZKPM.Client.Core.Cryptography;
using EZKPM.Client.Core.Services;

namespace EZKPM.Client.Desktop.Views
{
    public partial class RotationAssistantWindow : Window
    {
        private List<VaultAssetPayload> _expiredAssets;
        private readonly VaultCryptoService _cryptoService;
        private readonly VaultApiClient _apiClient;

        public RotationAssistantWindow()
        {
            InitializeComponent();
        }

        public RotationAssistantWindow(List<VaultAssetPayload> allAssets, VaultCryptoService cryptoService, VaultApiClient apiClient) : this()
        {
            _cryptoService = cryptoService;
            _apiClient = apiClient;
            _expiredAssets = allAssets.Where(a => a.IsExpired && a.AssetType == "Login" && !a.IsDeleted).ToList();
            
            var listBox = this.FindControl<ListBox>("ExpiredAssetsListBox");
            if (listBox != null)
            {
                listBox.ItemsSource = _expiredAssets;
            }
        }

        private async void RotateButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is VaultAssetPayload payload)
            {
                try
                {
                    btn.IsEnabled = false;

                    // 1. Generate new password
                    string newPassword = _cryptoService.GeneratePassword(payload.PasswordSettings);
                    
                    // 2. Zeige den Assisted Rotation Dialog an
                    var confirmDialog = new RotationConfirmationWindow(newPassword);
                    await confirmDialog.ShowDialog(this);

                    if (!confirmDialog.IsConfirmed)
                    {
                        btn.IsEnabled = true;
                        return; // User canceled the rotation
                    }

                    btn.Content = "⏳";
                    payload.Password = newPassword;

                    // 3. Encrypt and save (this resets ExpiresAt on the server)
                    var requestDto = _cryptoService.EncryptAsset(payload);
                    await _apiClient.UpdateAssetAsync(payload.TransientAssetId.Value, requestDto);

                    // 4. Remove from list
                    _expiredAssets.Remove(payload);
                    var listBox = this.FindControl<ListBox>("ExpiredAssetsListBox");
                    if (listBox != null)
                    {
                        listBox.ItemsSource = null;
                        listBox.ItemsSource = _expiredAssets;
                    }
                }
                catch (Exception ex)
                {
                    Program.LogDebug($"Rotation error: {ex.Message}");
                    btn.Content = "Fehler!";
                }
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close(true); // Return true to indicate we might have made changes
        }
    }
}
