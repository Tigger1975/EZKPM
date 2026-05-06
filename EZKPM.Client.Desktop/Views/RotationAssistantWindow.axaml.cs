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
                    btn.Content = "⏳";

                    // 1. Generate new password
                    string newPassword = _cryptoService.GeneratePassword(payload.PasswordSettings);
                    payload.Password = newPassword;
                    
                    // Note: In a fully automated Rotation Assistant, this would ALSO try to connect 
                    // to the target website (or use an API like Microsoft Graph for AD passwords) 
                    // to actually change the password remotely. For now, we rotate it in the Vault.

                    // 2. Encrypt and save (this resets ExpiresAt on the server)
                    var requestDto = _cryptoService.EncryptAsset(payload);
                    await _apiClient.UpdateAssetAsync(payload.TransientAssetId.Value, requestDto);

                    // 3. Remove from list
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
                    Console.WriteLine($"Rotation error: {ex.Message}");
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
