using System;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
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
    private readonly ObservableCollection<VaultTreeNode> _treeNodes = new();
    private Guid? _currentEditingAssetId = null;

    public MainWindow()
    {
        InitializeComponent();

        var httpClient = new HttpClient { BaseAddress = new Uri("http://localhost:5117") };
        _apiClient = new VaultApiClient(httpClient);
        _cryptoService = new VaultCryptoService(new HybridPqcKeyWrapper());

        AssetTreeView.ItemsSource = _treeNodes;
        
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
            
            BuildTree();
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Load Error: {ex.Message}";
            StatusTextBlock.Foreground = Avalonia.Media.Brushes.Red;
        }
    }

    private void BuildTree()
    {
        _treeNodes.Clear();
        var nodes = _decryptedAssets.ToDictionary(a => a.TransientAssetId.GetValueOrDefault(), a => new VaultTreeNode { Payload = a });

        foreach (var node in nodes.Values)
        {
            if (node.Payload.ParentFolderId.HasValue && nodes.TryGetValue(node.Payload.ParentFolderId.Value, out var parent))
            {
                parent.Children.Add(node);
            }
            else
            {
                _treeNodes.Add(node);
            }
        }
        
        // Populate ParentFolderComboBox
        var folders = _decryptedAssets.Where(a => a.AssetType == "Folder").ToList();
        folders.Insert(0, new VaultAssetPayload { Title = "-- Root --", TransientAssetId = null });
        ParentFolderComboBox.ItemsSource = folders;
    }

    private void AssetTreeView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AssetTreeView.SelectedItem is VaultTreeNode node)
        {
            var payload = node.Payload;
            _currentEditingAssetId = payload.TransientAssetId;
            EditorPanel.IsVisible = true;
            TitleTextBox.Text = payload.Title;
            UsernameTextBox.Text = payload.Username;
            PasswordTextBox.Text = payload.Password;
            UrlTextBox.Text = payload.Url;
            NotesTextBox.Text = payload.Notes;

            if (payload.AssetType == "Login") AssetTypeComboBox.SelectedIndex = 0;
            else if (payload.AssetType == "Payment") AssetTypeComboBox.SelectedIndex = 1;
            else if (payload.AssetType == "SecureNote") AssetTypeComboBox.SelectedIndex = 2;
            else if (payload.AssetType == "SSH Key") AssetTypeComboBox.SelectedIndex = 3;
            else if (payload.AssetType == "SSL Key") AssetTypeComboBox.SelectedIndex = 4;
            else if (payload.AssetType == "API Key") AssetTypeComboBox.SelectedIndex = 5;
            else AssetTypeComboBox.SelectedIndex = 6;

            ParentFolderComboBox.SelectedItem = (ParentFolderComboBox.ItemsSource as IEnumerable<VaultAssetPayload>)?.FirstOrDefault(f => f.TransientAssetId == payload.ParentFolderId);

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

    private void AssetTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CredentialsPanel != null)
        {
            CredentialsPanel.IsVisible = AssetTypeComboBox.SelectedIndex != 6; // Hide if "Folder"
        }
    }

    private void NewFolderButton_Click(object sender, RoutedEventArgs e)
    {
        ResetEditor();
        AssetTypeComboBox.SelectedIndex = 6; // Folder
    }

    private void NewAssetButton_Click(object sender, RoutedEventArgs e)
    {
        ResetEditor();
    }

    private void ResetEditor()
    {
        _currentEditingAssetId = null;
        if (AssetTreeView != null) AssetTreeView.SelectedItem = null;
        EditorPanel.IsVisible = true;
        TitleTextBox.Text = "";
        UsernameTextBox.Text = "";
        PasswordTextBox.Text = "";
        UrlTextBox.Text = "";
        NotesTextBox.Text = "";
        AssetTypeComboBox.SelectedIndex = 0;
        if (ParentFolderComboBox.Items.Count > 0) ParentFolderComboBox.SelectedIndex = 0;
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

    private async void CopyAllDetailsButton_Click(object sender, RoutedEventArgs e)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
        {
            var text = $"Title: {TitleTextBox.Text}\n" +
                       $"Type: {(AssetTypeComboBox.SelectedItem as ComboBoxItem)?.Content}\n" +
                       $"URL: {UrlTextBox.Text}\n" +
                       $"Username: {UsernameTextBox.Text}\n" +
                       $"Password: {PasswordTextBox.Text}\n" +
                       $"Notes: {NotesTextBox.Text}";
            
            await clipboard.SetTextAsync(text.Trim());
            StatusTextBlock.Text = "Gesamte Asset-Details in die Zwischenablage kopiert!";
            StatusTextBlock.Foreground = Avalonia.Media.Brushes.Green;
        }
    }

    private void DuplicateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentEditingAssetId != null)
        {
            _currentEditingAssetId = null;
            if (AssetTreeView != null) AssetTreeView.SelectedItem = null;
            TitleTextBox.Text += " (Kopie)";
            StatusTextBlock.Text = "Asset als Kopie vorbereitet. Klicke auf 'Save to Server' um es neu anzulegen.";
            StatusTextBlock.Foreground = Avalonia.Media.Brushes.Orange;
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
                ParentFolderId = (ParentFolderComboBox.SelectedItem as VaultAssetPayload)?.TransientAssetId,
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

    private Point _dragStartPoint;
    private bool _isDragging;

    private void TreeNode_PointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _dragStartPoint = e.GetPosition(null);
            _isDragging = false;
        }
    }

    private async void TreeNode_PointerMoved(object sender, PointerEventArgs e)
    {
        if (!_isDragging && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            var point = e.GetPosition(null);
            if (Math.Abs(point.X - _dragStartPoint.X) > 3 || Math.Abs(point.Y - _dragStartPoint.Y) > 3)
            {
                if (sender is Control control && control.DataContext is VaultTreeNode node)
                {
                    _isDragging = true;
                    var dragData = new DataObject();
                    dragData.Set("DraggedNode", node);
                    
                    await DragDrop.DoDragDrop(e, dragData, DragDropEffects.Move);
                    _isDragging = false;
                }
            }
        }
    }

    private void AssetTreeView_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.Contains("DraggedNode") && e.Source is Control control && control.DataContext is VaultTreeNode targetNode)
        {
            var draggedNode = e.Data.Get("DraggedNode") as VaultTreeNode;
            if (draggedNode != null && targetNode.IsFolder && draggedNode != targetNode)
            {
                e.DragEffects = DragDropEffects.Move;
                return;
            }
        }
        e.DragEffects = DragDropEffects.None;
    }

    private async void AssetTreeView_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.Contains("DraggedNode") && e.Source is Control control && control.DataContext is VaultTreeNode targetNode)
        {
            var draggedNode = e.Data.Get("DraggedNode") as VaultTreeNode;
            if (draggedNode != null && targetNode.IsFolder && draggedNode != targetNode)
            {
                draggedNode.Payload.ParentFolderId = targetNode.Payload.TransientAssetId;

                try
                {
                    var requestDto = _cryptoService.EncryptAsset(draggedNode.Payload);
                    await _apiClient.UpdateAssetAsync(draggedNode.Payload.TransientAssetId.Value, requestDto);
                    
                    await LoadAssetsAsync();
                    StatusTextBlock.Text = $"'{draggedNode.Title}' in '{targetNode.Title}' verschoben!";
                    StatusTextBlock.Foreground = Avalonia.Media.Brushes.Green;
                }
                catch (Exception ex)
                {
                    StatusTextBlock.Text = $"Fehler beim Verschieben: {ex.Message}";
                    StatusTextBlock.Foreground = Avalonia.Media.Brushes.Red;
                }
            }
        }
    }
}