using System;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Input.Platform;
using EZKPM.Client.Core.Cryptography;
using EZKPM.Client.Core.Services;
using EZKPM.Shared.Contracts;
using Avalonia.Platform.Storage;
using System.IO;
using EZKPM.Client.Desktop.Services;

namespace EZKPM.Client.Desktop;

public partial class MainWindow : Window
{
    private readonly VaultApiClient _apiClient;
    private readonly VaultCryptoService _cryptoService;
    private readonly ObservableCollection<VaultAssetPayload> _decryptedAssets = new();
    private readonly ObservableCollection<VaultTreeNode> _treeNodes = new();
    private readonly HashSet<Guid> _expandedFolderIds = new();
    private readonly BrowserBridgeServer _bridgeServer;
    
    public MainWindow(Views.SplashScreenWindow? splash = null)
    {
        InitializeComponent();

        var httpClient = new HttpClient { BaseAddress = new Uri("http://localhost:5000") };
        _apiClient = new VaultApiClient(httpClient);
        _cryptoService = new VaultCryptoService(new HybridPqcKeyWrapper());

        AssetTreeView.ItemsSource = _treeNodes;

        _bridgeServer = new BrowserBridgeServer(() => _decryptedAssets, RequestAuditAsync);
        _bridgeServer.Start();

        // Auto-load assets on startup
        _ = LoadAssetsAndShowAsync(splash);
    }

    private async Task<bool> RequestAuditAsync(Guid assetId)
    {
        var tcs = new TaskCompletionSource<bool>();
        Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                var dialog = new Views.AuditDialog();
                // If MainWindow is visible, show as dialog, else just show it
                var result = await dialog.ShowAuditPromptAsync();
                
                if (result.IsAuthorized)
                {
                    // Update AuditLog on server (FA 22)
                    var asset = _decryptedAssets.FirstOrDefault(a => a.TransientAssetId == assetId);
                    if (asset != null)
                    {
                        byte[] previousHash = await _apiClient.GetLatestAuditHashAsync(assetId);
                        var req = _cryptoService.CreateAuditLogRequest($"Order: {result.OrderId}, Amount: {result.Amount}", previousHash);
                        await _apiClient.AppendAuditLogAsync(assetId, req);
                    }
                }
                
                tcs.SetResult(result.IsAuthorized);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Audit Request Error: {ex.Message}");
                tcs.SetResult(false);
            }
        });
        return await tcs.Task;
    }

    private async Task LoadAssetsAndShowAsync(Views.SplashScreenWindow? splash)
    {
        if (splash != null) splash.UpdateStatus("Verbinde mit Vault Server...", 20);
        await Task.Delay(500); // Artificial delay to let the splash screen render

        await LoadAssetsAsync(splash);
        
        if (splash != null)
        {
            splash.UpdateStatus("Starte Desktop Client...", 100);
            await Task.Delay(500); // Artificial delay to ensure user sees 100%
            
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = this;
            }
            this.Show();
            splash.Close();
        }
    }

    private async Task LoadAssetsAsync(Views.SplashScreenWindow? splash = null)
    {
        try
        {
            var serverAssets = await _apiClient.GetAllAssetsAsync();
            _decryptedAssets.Clear();

            int total = serverAssets.Count;
            int current = 0;
            foreach (var dto in serverAssets)
            {
                current++;
                if (splash != null && total > 0)
                {
                    double progress = 20.0 + (80.0 * ((double)current / total));
                    splash.UpdateStatus($"Entschlüssele Asset {current} von {total}...", progress);
                }

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
            ShowStatus($"Load Error: {ex.Message}", isError: true);
        }
    }

    private void BuildTree()
    {
        // Save expanded state before clearing
        _expandedFolderIds.Clear();
        void Traverse(IEnumerable<VaultTreeNode> nodes)
        {
            foreach (var n in nodes)
            {
                if (n.IsExpanded && n.Payload.TransientAssetId.HasValue)
                    _expandedFolderIds.Add(n.Payload.TransientAssetId.Value);
                Traverse(n.Children);
            }
        }
        Traverse(_treeNodes);

        _treeNodes.Clear();
        var nodesMap = _decryptedAssets.Where(a => a.AssetType == "Folder" && !a.IsDeleted).ToDictionary(a => a.TransientAssetId.GetValueOrDefault(), a => new VaultTreeNode { Payload = a });

        foreach (var node in nodesMap.Values)
        {
            if (node.Payload.TransientAssetId.HasValue && _expandedFolderIds.Contains(node.Payload.TransientAssetId.Value))
            {
                node.IsExpanded = true;
            }

            if (node.Payload.ParentFolderId.HasValue && nodesMap.TryGetValue(node.Payload.ParentFolderId.Value, out var parent))
            {
                parent.Children.Add(node);
            }
            else
            {
                _treeNodes.Add(node);
            }
        }
        
        // Update DataGrid with root assets initially
        ComputePaths();
        UpdateDataGrid();
    }

    private void ComputePaths()
    {
        var folderMap = _decryptedAssets.Where(a => a.AssetType == "Folder").ToDictionary(a => a.TransientAssetId.GetValueOrDefault(), a => a);
        
        foreach (var asset in _decryptedAssets)
        {
            if (asset.AssetType == "Folder") continue;

            var pathParts = new List<string>();
            var currentId = asset.ParentFolderId;
            while (currentId.HasValue && folderMap.TryGetValue(currentId.Value, out var parentFolder))
            {
                pathParts.Insert(0, parentFolder.Title);
                currentId = parentFolder.ParentFolderId;
            }

            asset.FullPath = pathParts.Count > 0 ? string.Join(" / ", pathParts) : "/ (Wurzelverzeichnis)";
        }
    }

    private void AssetTreeView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AssetTreeView.SelectedItem is VaultTreeNode node)
        {
            MainTabControl.SelectedIndex = 0;
        }
        UpdateDataGrid();
    }

    private void UpdateDataGrid()
    {
        if (!this.IsInitialized || AssetsDataGrid == null || _decryptedAssets == null) return;
        
        Guid? parentId = null;
        if (AssetTreeView?.SelectedItem is VaultTreeNode node)
        {
            parentId = node.Payload.TransientAssetId;
        }

        string searchText = SearchTextBox?.Text?.ToLower() ?? "";
        string selectedType = (TypeFilterComboBox?.SelectedItem as Avalonia.Controls.ComboBoxItem)?.Content?.ToString() ?? "Alle Typen";

        bool isSearching = !string.IsNullOrWhiteSpace(searchText);

        var filtered = _decryptedAssets.Where(a => a.AssetType != "Folder");

        if (selectedType == "Papierkorb")
        {
            filtered = filtered.Where(a => a.IsDeleted);
        }
        else
        {
            filtered = filtered.Where(a => !a.IsDeleted);
            
            if (!isSearching)
            {
                filtered = filtered.Where(a => a.ParentFolderId == parentId);
            }

            if (selectedType == "Abgelaufen (Rotation)")
            {
                filtered = filtered.Where(a => a.IsExpired);
            }
            else if (selectedType != "Alle Typen")
            {
                filtered = filtered.Where(a => a.AssetType == selectedType);
            }
        }

        if (isSearching)
        {
            filtered = filtered.Where(a => 
                (a.Title != null && a.Title.ToLower().Contains(searchText)) ||
                (a.DetailedDescription != null && a.DetailedDescription.ToLower().Contains(searchText)) ||
                (a.Url != null && a.Url.ToLower().Contains(searchText))
            );
        }

        AssetsDataGrid.ItemsSource = filtered.ToList();
    }

    private void SearchTextBox_TextChanged(object sender, Avalonia.Controls.TextChangedEventArgs e)
    {
        UpdateDataGrid();
    }

    private void TypeFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!this.IsInitialized) return;
        UpdateDataGrid();
        
        if (RestoreSelectedAssetsButton != null && TypeFilterComboBox != null)
        {
            string selectedType = (TypeFilterComboBox.SelectedItem as Avalonia.Controls.ComboBoxItem)?.Content?.ToString() ?? "Alle Typen";
            RestoreSelectedAssetsButton.IsVisible = selectedType == "Papierkorb";
        }
    }

    private async void DeleteSelectedAssets_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (AssetsDataGrid.SelectedItems.Count == 0) return;

        var items = AssetsDataGrid.SelectedItems.Cast<VaultAssetPayload>().ToList();
        
        var dialog = new Views.ConfirmationDialog($"Möchten Sie {items.Count} Asset(s) wirklich in den Papierkorb verschieben?");
        var result = await dialog.ShowDialogAsync(this);
        if (!result) return;

        int count = 0;
        foreach (var item in items)
        {
            if (item.TransientAssetId.HasValue)
            {
                try
                {
                    await _apiClient.DeleteAssetAsync(item.TransientAssetId.Value);
                    count++;
                }
                catch (Exception ex)
                {
                    ShowStatus($"Fehler beim Löschen von {item.Title}: {ex.Message}");
                }
            }
        }

        if (count > 0)
        {
            ShowStatus($"{count} Asset(s) in den Papierkorb verschoben.");
            await LoadAssetsAsync();
        }
    }

    private async void RestoreSelectedAssets_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (AssetsDataGrid.SelectedItems.Count == 0) return;

        var items = AssetsDataGrid.SelectedItems.Cast<VaultAssetPayload>().ToList();
        
        int count = 0;
        foreach (var item in items)
        {
            if (item.TransientAssetId.HasValue)
            {
                try
                {
                    await _apiClient.RestoreAssetAsync(item.TransientAssetId.Value);
                    count++;
                }
                catch (Exception ex)
                {
                    ShowStatus($"Fehler beim Wiederherstellen von {item.Title}: {ex.Message}");
                }
            }
        }

        if (count > 0)
        {
            ShowStatus($"{count} Asset(s) wiederhergestellt.");
            await LoadAssetsAsync();
        }
    }

    private async void ImportKeePass_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            var files = await this.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "KeePass / CSV Datei auswählen",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("KeePass Database") { Patterns = new[] { "*.kdbx" } },
                    new FilePickerFileType("KeePass XML") { Patterns = new[] { "*.xml" } },
                    new FilePickerFileType("CSV Datei") { Patterns = new[] { "*.csv" } },
                    new FilePickerFileType("Alle Dateien") { Patterns = new[] { "*.*" } }
                }
            });

            if (files.Count >= 1)
            {
                var file = files[0];
                string password = null;
                string keyFilePath = null;
                IPasswordDbImporter importer;
                
                if (file.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                {
                    importer = new CsvImporter();
                }
                else if (file.Name.EndsWith(".kdbx", StringComparison.OrdinalIgnoreCase))
                {
                    importer = new KdbxImporter();
                    var pwdDialog = new Views.PasswordPromptDialog();
                    var result = await pwdDialog.ShowDialogAsync(this);
                    
                    if (result == null || (string.IsNullOrEmpty(result.Password) && string.IsNullOrEmpty(result.KeyFilePath)))
                    {
                        ShowStatus("Import abgebrochen: Kein Passwort / keine Key-Datei eingegeben.");
                        return;
                    }
                    
                    password = result.Password;
                    keyFilePath = result.KeyFilePath;
                }
                else
                {
                    importer = new KeePassXmlImporter();
                }

                await using var stream = await file.OpenReadAsync();
                var importedPayloads = await importer.ImportAsync(stream, password, keyFilePath);

                int successCount = 0;
                var idMap = new Dictionary<Guid, Guid>();

                var currentUserSid = Services.AdSearchService.GetCurrentUser().Sid;

                foreach (var payload in importedPayloads)
                {
                    try
                    {
                        if (payload.ParentFolderId.HasValue && idMap.TryGetValue(payload.ParentFolderId.Value, out var realParentId))
                        {
                            payload.ParentFolderId = realParentId;
                        }

                        // Ensure we have owner ACLs, otherwise the server rejects it or it becomes invisible
                        if (payload.Acls.Count == 0)
                        {
                            payload.Acls.Add(new AclEntryDto { AdSid = currentUserSid, PermissionLevel = 3 });
                        }

                        var requestDto = _cryptoService.EncryptAsset(payload);
                        Guid realId = await _apiClient.CreateAssetAsync(requestDto);
                        
                        if (payload.TransientAssetId.HasValue)
                        {
                            idMap[payload.TransientAssetId.Value] = realId;
                        }

                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Fehler beim Importieren eines Assets ({payload.Title}): {ex.Message}");
                    }
                }

                ShowStatus($"Import abgeschlossen: {successCount} Assets importiert.");
                await LoadAssetsAsync();
            }
        }
        catch (Exception ex)
        {
            ShowStatus($"Fehler beim Import: {ex.Message}", isError: true);
        }
    }

    private void AssetsDataGrid_DoubleTapped(object sender, Avalonia.Input.TappedEventArgs e)
    {
        if (sender is DataGrid grid && grid.SelectedItem is VaultAssetPayload payload)
        {
            var editor = new Views.AssetEditorWindow(payload, _decryptedAssets.ToList());
            editor.AssetSaved += async (s, args) => await LoadAssetsAsync();
            editor.Show();
        }
    }

    private void EditFolderMenuItem_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is VaultTreeNode node)
        {
            var editor = new Views.AssetEditorWindow(node.Payload, _decryptedAssets.ToList());
            editor.AssetSaved += async (s, args) => await LoadAssetsAsync();
            editor.Show();
        }
    }

    private async void DeleteFolderMenuItem_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is VaultTreeNode node)
        {
            if (node.Payload.TransientAssetId.HasValue)
            {
                var dialog = new Views.ConfirmationDialog($"Möchten Sie den Ordner '{node.Title}' und alle seine Inhalte wirklich in den Papierkorb verschieben?");
                var result = await dialog.ShowDialogAsync(this);
                if (!result) return;
                
                try
                {
                    await DeleteAssetRecursiveAsync(node.Payload.TransientAssetId.Value);
                    ShowStatus($"Ordner '{node.Title}' gelöscht.");
                    await LoadAssetsAsync();
                }
                catch (Exception ex)
                {
                    ShowStatus($"Fehler beim Löschen des Ordners: {ex.Message}", isError: true);
                }
            }
        }
    }

    private async Task DeleteAssetRecursiveAsync(Guid folderId)
    {
        var children = _decryptedAssets.Where(a => a.ParentFolderId == folderId).ToList();
        foreach (var child in children)
        {
            if (child.TransientAssetId.HasValue)
            {
                if (child.AssetType == "Folder")
                {
                    await DeleteAssetRecursiveAsync(child.TransientAssetId.Value);
                }
                else
                {
                    await _apiClient.DeleteAssetAsync(child.TransientAssetId.Value);
                }
            }
        }
        await _apiClient.DeleteAssetAsync(folderId);
    }



    private void NewFolderButton_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var selectedNode = AssetTreeView?.SelectedItem as VaultTreeNode;
        var payload = new VaultAssetPayload() { AssetType = "Folder" };
        if (selectedNode != null && selectedNode.Payload.AssetType == "Folder") {
            payload.ParentFolderId = selectedNode.Payload.TransientAssetId;
        }
        var editor = new Views.AssetEditorWindow(payload, _decryptedAssets.ToList());
        editor.AssetSaved += async (s, args) => await LoadAssetsAsync();
        editor.Show();
    }
    private void NewAssetButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedNode = AssetTreeView?.SelectedItem as VaultTreeNode;
        var payload = new VaultAssetPayload();
        if (selectedNode != null && selectedNode.Payload.AssetType == "Folder") {
            payload.ParentFolderId = selectedNode.Payload.TransientAssetId;
        }
        var editor = new Views.AssetEditorWindow(payload, _decryptedAssets.ToList());
        editor.AssetSaved += async (s, args) => await LoadAssetsAsync();
        editor.Show();
    }
    private Point _dragStartPoint;
    private bool _isDragging;
    private PointerPressedEventArgs? _dragStartEventArgs;
    private static VaultTreeNode? s_draggedNode;

    private void TreeNode_PointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _dragStartPoint = e.GetPosition(null);
            _isDragging = false;
            _dragStartEventArgs = e;
        }
    }

    private async void TreeNode_PointerMoved(object sender, PointerEventArgs e)
    {
        if (!_isDragging && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && _dragStartEventArgs != null)
        {
            var point = e.GetPosition(null);
            if (Math.Abs(point.X - _dragStartPoint.X) > 3 || Math.Abs(point.Y - _dragStartPoint.Y) > 3)
            {
                if (sender is Control control && control.DataContext is VaultTreeNode node)
                {
                    _isDragging = true;
                    s_draggedNode = node;
                    
                    var dragData = new DataTransfer();
                    dragData.Add(DataTransferItem.CreateText("vault-node"));
                    
                    await DragDrop.DoDragDropAsync(_dragStartEventArgs, dragData, DragDropEffects.Move);
                    
                    s_draggedNode = null;
                    _isDragging = false;
                    _dragStartEventArgs = null;
                }
            }
        }
    }

    private void AssetTreeView_DragOver(object sender, DragEventArgs e)
    {
        if (s_draggedNode != null && e.Source is Control control && control.DataContext is VaultTreeNode targetNode)
        {
            if (targetNode.IsFolder && s_draggedNode != targetNode)
            {
                e.DragEffects = DragDropEffects.Move;
                return;
            }
        }
        e.DragEffects = DragDropEffects.None;
    }

    private async void AssetTreeView_Drop(object sender, DragEventArgs e)
    {
        if (s_draggedNode != null && e.Source is Control control && control.DataContext is VaultTreeNode targetNode)
        {
            if (targetNode.IsFolder && s_draggedNode != targetNode)
            {
                var draggedNode = s_draggedNode;
                s_draggedNode = null;
                
                draggedNode.Payload.ParentFolderId = targetNode.Payload.TransientAssetId;

                try
                {
                    if (draggedNode.Payload.TransientAssetId.HasValue)
                    {
                        var requestDto = _cryptoService.EncryptAsset(draggedNode.Payload);
                        await _apiClient.UpdateAssetAsync(draggedNode.Payload.TransientAssetId.Value, requestDto);
                        
                        await LoadAssetsAsync();
                        ShowStatus($"'{draggedNode.Title}' in '{targetNode.Title}' verschoben!");
                    }
                }
                catch (Exception ex)
                {
                    ShowStatus($"Fehler beim Verschieben: {ex.Message}", isError: true);
                }
            }
        }
    }

    private void ShowStatus(string message, bool isError = false, bool isWarning = false)
    {
        StatusTextBlock.Text = message;
        if (isError) StatusTextBlock.Foreground = Avalonia.Media.Brushes.Red;
        else if (isWarning) StatusTextBlock.Foreground = Avalonia.Media.Brushes.Orange;
        else StatusTextBlock.Foreground = Avalonia.Media.Brushes.Green;

        if (string.IsNullOrEmpty(message)) return;

        try
        {
            var logFile = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ezkpm_app.log");
            var level = isError ? "ERROR" : (isWarning ? "WARN" : "INFO");
            var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}\n";
            System.IO.File.AppendAllText(logFile, logEntry);
        }
        catch { }
    }

}