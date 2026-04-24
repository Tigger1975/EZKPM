using System;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using EZKPM.Client.Core.Cryptography;
using EZKPM.Client.Core.Services;
using EZKPM.Shared.Contracts;

namespace EZKPM.Client.Desktop;

public partial class MainWindow : Window
{
    private readonly VaultApiClient _apiClient;
    private readonly VaultCryptoService _cryptoService;
    private readonly ObservableCollection<VaultAssetPayload> _decryptedAssets = new();

    public MainWindow()
    {
        InitializeComponent();

        var httpClient = new HttpClient { BaseAddress = new Uri("http://localhost:5117") };
        _apiClient = new VaultApiClient(httpClient);
        _cryptoService = new VaultCryptoService(new HybridPqcKeyWrapper());

        AssetListBox.ItemsSource = _decryptedAssets;
        
        // Auto-load assets on startup
        _ = LoadAssetsAsync();
    }

    private async Task LoadAssetsAsync()
    {
        try
        {
            var serverAssets = await _apiClient.GetAllAssetsAsync();
            _decryptedAssets.Clear();

            foreach (var dto in serverAssets)
            {
                try
                {
                    var payload = _cryptoService.DecryptAsset(dto);
                    _decryptedAssets.Add(payload);
                }
                catch (Exception ex)
                {
                    // If decryption fails (wrong key, expired), skip or show error placeholder
                    Console.WriteLine($"Decryption failed for asset {dto.AssetId}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Load Error: {ex.Message}";
            StatusTextBlock.Foreground = Avalonia.Media.Brushes.Red;
        }
    }

    private void AssetListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AssetListBox.SelectedItem is VaultAssetPayload payload)
        {
            EditorPanel.IsVisible = true;
            TitleTextBox.Text = payload.Title;
            UsernameTextBox.Text = payload.Username;
            PasswordTextBox.Text = payload.Password;
            UrlTextBox.Text = payload.Url;
            NotesTextBox.Text = payload.Notes;

            if (payload.AssetType == "Login") AssetTypeComboBox.SelectedIndex = 0;
            else if (payload.AssetType == "Payment") AssetTypeComboBox.SelectedIndex = 1;
            else AssetTypeComboBox.SelectedIndex = 2;
        }
    }

    private void NewAssetButton_Click(object sender, RoutedEventArgs e)
    {
        AssetListBox.SelectedItem = null;
        EditorPanel.IsVisible = true;
        TitleTextBox.Text = "";
        UsernameTextBox.Text = "";
        PasswordTextBox.Text = "";
        UrlTextBox.Text = "";
        NotesTextBox.Text = "";
        AssetTypeComboBox.SelectedIndex = 0;
        StatusTextBlock.Text = "";
    }

    private void GeneratePasswordButton_Click(object sender, RoutedEventArgs e)
    {
        PasswordTextBox.Text = _cryptoService.GeneratePassword(20, true);
    }

    private void ShowPasswordCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
    {
        if (ShowPasswordCheckBox.IsChecked == true)
        {
            PasswordTextBox.PasswordChar = '\0'; // Show text
        }
        else
        {
            PasswordTextBox.PasswordChar = '*'; // Hide text
        }
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var payload = new VaultAssetPayload
            {
                Title = TitleTextBox.Text ?? "Untitled",
                AssetType = (AssetTypeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Login",
                Username = UsernameTextBox.Text ?? "",
                Password = PasswordTextBox.Text ?? "",
                Url = UrlTextBox.Text ?? "",
                Notes = NotesTextBox.Text ?? ""
            };

            // 1. Encrypt Payload locally
            var requestDto = _cryptoService.EncryptAsset(payload);

            // 2. Send to Server
            Guid newId = await _apiClient.CreateAssetAsync(requestDto);

            StatusTextBlock.Text = "Saved successfully!";
            StatusTextBlock.Foreground = Avalonia.Media.Brushes.Green;

            // 3. Reload list
            await LoadAssetsAsync();
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Save Error: {ex.Message}";
            StatusTextBlock.Foreground = Avalonia.Media.Brushes.Red;
        }
    }
}