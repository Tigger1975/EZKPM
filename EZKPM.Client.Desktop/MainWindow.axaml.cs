using System;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input.Platform;
using EZKPM.Client.Core.Cryptography;
using EZKPM.Client.Core.Services;
using EZKPM.Shared.Contracts;

namespace EZKPM.Client.Desktop;

public partial class MainWindow : Window
{
    private readonly VaultApiClient _apiClient;
    private readonly VaultCryptoService _cryptoService;
    private readonly ObservableCollection<VaultAssetPayload> _decryptedAssets = new();
    private Guid? _currentEditingAssetId = null;

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
                    if (payload != null) _decryptedAssets.Add(payload);
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
            _currentEditingAssetId = payload.TransientAssetId;
            EditorPanel.IsVisible = true;
            TitleTextBox.Text = payload.Title;
            UsernameTextBox.Text = payload.Username;
            PasswordTextBox.Text = payload.Password;
            UrlTextBox.Text = payload.Url;
            NotesTextBox.Text = payload.Notes;

            if (payload.AssetType == "Login") AssetTypeComboBox.SelectedIndex = 0;
            else if (payload.AssetType == "Payment") AssetTypeComboBox.SelectedIndex = 1;
            else AssetTypeComboBox.SelectedIndex = 2;

            // Password Settings
            GenLengthControl.Value = payload.PasswordSettings.Length;
            GenUpperCheck.IsChecked = payload.PasswordSettings.UseUppercase;
            GenLowerCheck.IsChecked = payload.PasswordSettings.UseLowercase;
            GenNumberCheck.IsChecked = payload.PasswordSettings.UseNumbers;
            GenSymbolCheck.IsChecked = payload.PasswordSettings.UseSymbols;

            // Login Flow Settings
            if (payload.LoginFlow.Method == "AutoLearn") LoginMethodComboBox.SelectedIndex = 0;
            else if (payload.LoginFlow.Method == "OneStep") LoginMethodComboBox.SelectedIndex = 1;
            else if (payload.LoginFlow.Method == "TwoStep") LoginMethodComboBox.SelectedIndex = 2;
            else LoginMethodComboBox.SelectedIndex = 3;

            AutoLearnEnabledCheck.IsChecked = payload.LoginFlow.AutoLearnEnabled;
            DomUserTextBox.Text = payload.LoginFlow.UsernameSelector;
            DomPassTextBox.Text = payload.LoginFlow.PasswordSelector;
            DomNextTextBox.Text = payload.LoginFlow.NextButtonSelector;
            DomSubmitTextBox.Text = payload.LoginFlow.SubmitButtonSelector;
        }
    }

    private void NewAssetButton_Click(object sender, RoutedEventArgs e)
    {
        _currentEditingAssetId = null;
        AssetListBox.SelectedItem = null;
        EditorPanel.IsVisible = true;
        TitleTextBox.Text = "";
        UsernameTextBox.Text = "";
        PasswordTextBox.Text = "";
        UrlTextBox.Text = "";
        NotesTextBox.Text = "";
        AssetTypeComboBox.SelectedIndex = 0;
        StatusTextBlock.Text = "";
        
        // Defaults
        GenLengthControl.Value = 20;
        GenUpperCheck.IsChecked = true;
        GenLowerCheck.IsChecked = true;
        GenNumberCheck.IsChecked = true;
        GenSymbolCheck.IsChecked = true;

        LoginMethodComboBox.SelectedIndex = 0;
        AutoLearnEnabledCheck.IsChecked = true;
        DomUserTextBox.Text = "";
        DomPassTextBox.Text = "";
        DomNextTextBox.Text = "";
        DomSubmitTextBox.Text = "";
    }

    private async void CopyUsernameButton_Click(object sender, RoutedEventArgs e)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
        {
            await clipboard.SetTextAsync(UsernameTextBox.Text ?? "");
            StatusTextBlock.Text = "Username in die Zwischenablage kopiert!";
            StatusTextBlock.Foreground = Avalonia.Media.Brushes.Green;
        }
    }

    private async void CopyPasswordButton_Click(object sender, RoutedEventArgs e)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
        {
            await clipboard.SetTextAsync(PasswordTextBox.Text ?? "");
            StatusTextBlock.Text = "Passwort in die Zwischenablage kopiert!";
            StatusTextBlock.Foreground = Avalonia.Media.Brushes.Green;
        }
    }

    private void GeneratePasswordButton_Click(object sender, RoutedEventArgs e)
    {
        var config = new PasswordGeneratorConfig
        {
            Length = (int)GenLengthControl.Value.GetValueOrDefault(20),
            UseUppercase = GenUpperCheck.IsChecked == true,
            UseLowercase = GenLowerCheck.IsChecked == true,
            UseNumbers = GenNumberCheck.IsChecked == true,
            UseSymbols = GenSymbolCheck.IsChecked == true
        };
        PasswordTextBox.Text = _cryptoService.GeneratePassword(config);
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
            string loginMethodStr = "AutoLearn";
            if (LoginMethodComboBox.SelectedIndex == 1) loginMethodStr = "OneStep";
            else if (LoginMethodComboBox.SelectedIndex == 2) loginMethodStr = "TwoStep";
            else if (LoginMethodComboBox.SelectedIndex == 3) loginMethodStr = "BasicAuth";

            var payload = new VaultAssetPayload
            {
                TransientAssetId = _currentEditingAssetId,
                Title = TitleTextBox.Text ?? "Untitled",
                AssetType = (AssetTypeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Login",
                Username = UsernameTextBox.Text ?? "",
                Password = PasswordTextBox.Text ?? "",
                Url = UrlTextBox.Text ?? "",
                Notes = NotesTextBox.Text ?? "",

                PasswordSettings = new PasswordGeneratorConfig
                {
                    Length = (int)GenLengthControl.Value.GetValueOrDefault(20),
                    UseUppercase = GenUpperCheck.IsChecked == true,
                    UseLowercase = GenLowerCheck.IsChecked == true,
                    UseNumbers = GenNumberCheck.IsChecked == true,
                    UseSymbols = GenSymbolCheck.IsChecked == true
                },

                LoginFlow = new LoginFlowConfig
                {
                    Method = loginMethodStr,
                    AutoLearnEnabled = AutoLearnEnabledCheck.IsChecked == true,
                    UsernameSelector = DomUserTextBox.Text ?? "",
                    PasswordSelector = DomPassTextBox.Text ?? "",
                    NextButtonSelector = DomNextTextBox.Text ?? "",
                    SubmitButtonSelector = DomSubmitTextBox.Text ?? ""
                }
            };

            // 1. Encrypt Payload locally
            var requestDto = _cryptoService.EncryptAsset(payload);

            // 2. Send to Server (Update or Create)
            if (_currentEditingAssetId.HasValue)
            {
                await _apiClient.UpdateAssetAsync(_currentEditingAssetId.Value, requestDto);
                StatusTextBlock.Text = "Updated successfully!";
            }
            else
            {
                Guid newId = await _apiClient.CreateAssetAsync(requestDto);
                _currentEditingAssetId = newId;
                StatusTextBlock.Text = "Saved successfully!";
            }
            
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