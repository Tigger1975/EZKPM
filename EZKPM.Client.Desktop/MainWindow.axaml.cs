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

namespace EZKPM.Client.Desktop;

public partial class MainWindow : Window
{
    private readonly VaultApiClient _apiClient;
    private readonly VaultCryptoService _cryptoService;
    private readonly ObservableCollection<VaultAssetPayload> _decryptedAssets = new();
    private readonly ObservableCollection<VaultTreeNode> _treeNodes = new();
    private readonly HashSet<Guid> _expandedFolderIds = new();
    
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
        var nodesMap = _decryptedAssets.Where(a => a.AssetType == "Folder").ToDictionary(a => a.TransientAssetId.GetValueOrDefault(), a => new VaultTreeNode { Payload = a });

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
        // Update DataGrid with root assets initially
        UpdateDataGrid();
        
        
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
        if (AssetsDataGrid == null) return;
        
        Guid? parentId = null;
        if (AssetTreeView?.SelectedItem is VaultTreeNode node)
        {
            parentId = node.Payload.TransientAssetId;
        }

        var searchTextBox = this.FindControl<Avalonia.Controls.TextBox>("SearchTextBox");
        var typeFilterComboBox = this.FindControl<Avalonia.Controls.ComboBox>("TypeFilterComboBox");

        string searchText = searchTextBox?.Text?.ToLower() ?? "";
        string selectedType = (typeFilterComboBox?.SelectedItem as Avalonia.Controls.ComboBoxItem)?.Content?.ToString() ?? "Alle Typen";

        var filtered = _decryptedAssets.Where(a => a.AssetType != "Folder" && a.ParentFolderId == parentId);

        if (selectedType != "Alle Typen")
        {
            filtered = filtered.Where(a => a.AssetType == selectedType);
        }

        if (!string.IsNullOrWhiteSpace(searchText))
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
        UpdateDataGrid();
    }

    private async void DeleteSelectedAssets_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (AssetsDataGrid?.SelectedItems == null) return;
        
        var selected = AssetsDataGrid.SelectedItems.Cast<VaultAssetPayload>().ToList();
        if (selected.Count == 0) return;
        
        foreach (var asset in selected)
        {
            if (asset.TransientAssetId.HasValue)
            {
                try {
                    await _apiClient.DeleteAssetAsync(asset.TransientAssetId.Value);
                } catch (Exception ex) {
                    ShowStatus($"Fehler beim Löschen: {ex.Message}", isError: true);
                }
            }
        }
        await LoadAssetsAsync();
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
                    var requestDto = _cryptoService.EncryptAsset(draggedNode.Payload);
                    await _apiClient.UpdateAssetAsync(draggedNode.Payload.TransientAssetId.Value, requestDto);
                    
                    await LoadAssetsAsync();
                    ShowStatus($"'{draggedNode.Title}' in '{targetNode.Title}' verschoben!");
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