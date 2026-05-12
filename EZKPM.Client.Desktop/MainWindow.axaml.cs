#nullable enable
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
    private string HashSid(string sid)
    {
        if (string.IsNullOrEmpty(sid)) return sid;
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        return Convert.ToBase64String(sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(sid)));
    }

    private readonly VaultApiClient _apiClient;
    private readonly VaultCryptoService _cryptoService;
    private readonly ObservableCollection<VaultAssetPayload> _decryptedAssets = new();
    private readonly ObservableCollection<VaultTreeNode> _treeNodes = new();
    private readonly HashSet<Guid> _expandedFolderIds = new();
    private readonly HashSet<Guid> _failedDecryptionIds = new();
    private readonly BrowserBridgeServer _bridgeServer;
    private readonly LocalCredentialsBroker _localBroker;
    private readonly SsoSyncClient _ssoSyncClient;
    
    public MainWindow(VaultCryptoService cryptoService, Views.StartupWindow? splash = null)
    {
        InitializeComponent();

        var handler = new HttpClientHandler {  };
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri(EZKPM.Client.Desktop.Services.ConfigurationManager.CurrentConfig.ServerUrl) };
        _apiClient = new VaultApiClient(httpClient);
        _cryptoService = cryptoService;

        AssetTreeView.ItemsSource = _treeNodes;

        _bridgeServer = new BrowserBridgeServer(() => _decryptedAssets, RequestAuditAsync);
        _bridgeServer.OnCredentialProvided = (assetTitle) => ShowNotification(assetTitle);
        _bridgeServer.OnSaveNewCredentialRequested = (payload) => {
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => {
                this.Show();
                this.Activate();
                this.Topmost = true;
                this.Topmost = false;

                payload.ParentFolderId = GetPrivateFolderId();

        var editor = new Views.AssetEditorWindow(payload, _decryptedAssets.ToList(), _cryptoService, _apiClient);
                editor.AssetSaved += async (s, args) => await LoadAssetsAsync();
                editor.Show();
            });
        };

        _localBroker = new LocalCredentialsBroker(() => _decryptedAssets, RequestLocalAppApprovalAsync);
        _ssoSyncClient = new SsoSyncClient(ShowSsoApprovalDialogAsync, async () => {
            await LoadAssetsAsync();
        });
        
        var pageantService = new Services.PageantEmulatorService(new Microsoft.Extensions.Logging.Abstractions.NullLogger<Services.PageantEmulatorService>(), () => _decryptedAssets);
        pageantService.StartAsync(System.Threading.CancellationToken.None);
        this.Closed += (s, e) => pageantService.Dispose();
        
        // FA 14: Session Timer (Initialized in LoginWindow)
        this.AddHandler(Avalonia.Input.InputElement.PointerMovedEvent, (s, e) => Services.SessionManager.RegisterActivity(), Avalonia.Interactivity.RoutingStrategies.Tunnel);
        this.AddHandler(Avalonia.Input.InputElement.KeyDownEvent, (s, e) => Services.SessionManager.RegisterActivity(), Avalonia.Interactivity.RoutingStrategies.Tunnel);
        this.PropertyChanged += (s, e) => {
            if (e.Property.Name == nameof(WindowState))
            {
                Services.SessionManager.HandleWindowStateChanged(this.WindowState);
            }
        };

        _bridgeServer.Start();
        _localBroker.Start();

        // Auto-load assets on startup
        _ = LoadAssetsAndShowAsync(splash);

        // Daily vulnerability scan for admins
        StartVulnerabilityScanner();

        this.Closing += MainWindow_Closing;
    }

    private void StartVulnerabilityScanner()
    {
        var scanner = new Services.VulnerabilityScannerService(_apiClient);
        
        // Start a background timer that triggers every 1 hour (the scanner internally checks if 24 hours have passed)
        var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromHours(1) };
        timer.Tick += async (s, e) => 
        {
            await scanner.CheckForVulnerabilitiesAsync(async (report) => 
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
                {
                    var dialog = new Views.ConfirmationDialog(report);
                    await dialog.ShowDialogAsync(this);
                });
            });
        };
        timer.Start();

        // Run immediately on startup after a small delay
        Task.Run(async () => 
        {
            await Task.Delay(TimeSpan.FromSeconds(30));
            await scanner.CheckForVulnerabilitiesAsync(async (report) => 
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
                {
                    var dialog = new Views.ConfirmationDialog(report);
                    await dialog.ShowDialogAsync(this);
                });
            });
        });
    }

    private void MainWindow_Closing(object? sender, Avalonia.Controls.WindowClosingEventArgs e)
    {
        // Cancel the close operation and just hide the window,
        // so it can be restored from the system tray.
        e.Cancel = true;
        this.Hide();
        Services.SessionManager.HandleWindowStateChanged(this.WindowState, isTray: true);
    }

    private void ShowNotification(string assetTitle)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var notifyIcon = new System.Windows.Forms.NotifyIcon
                {
                    Icon = System.Drawing.SystemIcons.Information,
                    Visible = true,
                    BalloonTipTitle = "EZKPM Autofill",
                    BalloonTipText = $"Anmeldedaten für '{assetTitle}' an Browser übertragen.",
                    BalloonTipIcon = System.Windows.Forms.ToolTipIcon.Info
                };
                notifyIcon.ShowBalloonTip(3000);

                // Auto-dispose the icon after the tip is gone to prevent ghost icons
                Task.Delay(5000).ContinueWith(_ => notifyIcon.Dispose());
            }
            catch (Exception ex)
            {
                Program.LogDebug($"Failed to show notification: {ex.Message}");
            }
        });
    }

    private async Task<bool> RequestAuditAsync(Guid assetId, bool requireInteractive, string silentReason = "Auto-Logged Access")
    {
        var tcs = new TaskCompletionSource<bool>();
        var asset = _decryptedAssets.FirstOrDefault(a => a.TransientAssetId == assetId);
        if (asset == null) return false;

        if (requireInteractive)
        {
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
                        byte[] previousHash = await _apiClient.GetLatestAuditHashAsync(assetId);
                        var req = _cryptoService.CreateAuditLogRequest($"Order: {result.OrderId}, Amount: {result.Amount}", previousHash);
                        await _apiClient.AppendAuditLogAsync(assetId, req);
                    }
                    
                    tcs.SetResult(result.IsAuthorized);
                }
                catch (Exception ex)
                {
                    Program.LogDebug($"Audit Request Error: {ex.Message}");
                    tcs.SetResult(false);
                }
            });
        }
        else
        {
            try
            {
                byte[] previousHash = await _apiClient.GetLatestAuditHashAsync(assetId);
                var req = _cryptoService.CreateAuditLogRequest(silentReason, previousHash);
                await _apiClient.AppendAuditLogAsync(assetId, req);
                tcs.SetResult(true);
            }
            catch (Exception ex)
            {
                Program.LogDebug($"Silent Audit Logging Error: {ex.Message}");
                tcs.SetResult(false);
            }
        }
        
        return await tcs.Task;
    }

    private async Task<LocalAppApprovalResult> RequestLocalAppApprovalAsync(string processName, string assetTitle, string warningText)
    {
        var tcs = new TaskCompletionSource<LocalAppApprovalResult>();
        Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
        {
            var dialog = new Views.LocalAppApprovalDialog(processName, assetTitle, warningText);
            await dialog.ShowDialog(this);
            tcs.SetResult(new Services.LocalAppApprovalResult { IsApproved = dialog.Result.IsApproved, RememberTrust = dialog.Result.RememberTrust });
        });
        return await tcs.Task;
    }

    private async Task<bool> ShowSsoApprovalDialogAsync(string requestId, string appId, string originServerUrl)
    {
        var tcs = new TaskCompletionSource<bool>();
        Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
        {
            var dialog = new Views.SsoApprovalDialog(appId, originServerUrl);
            var result = await dialog.ShowDialogAsync(this);
            tcs.SetResult(result);
        });
        return await tcs.Task;
    }

    private async Task LoadAssetsAndShowAsync(Views.StartupWindow? splash)
    {
        if (splash != null) splash.UpdateStatus(EZKPM.Client.Desktop.Resources.AppStrings.Startup_StatusConnect, 20);
        await Task.Delay(500); // Artificial delay to let the splash screen render

        try
        {
            var currentUserSid = EZKPM.Client.Desktop.Services.AdSearchService.GetCurrentUser()?.Sid ?? "S-1-5-21-DUMMY-TEST-USER";
            string hashedSid = HashSid(currentUserSid);

            var groupSids = new System.Collections.Generic.List<string>();
            try
            {
                var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                if (identity.Groups != null)
                {
                    foreach (var group in identity.Groups)
                    {
                        groupSids.Add(HashSid(group.Value));
                    }
                }
            }
            catch { }

            bool isAuthenticated = await _apiClient.AuthenticateAsync(_cryptoService.IdentityKey, hashedSid, groupSids);
            
            if (!isAuthenticated)
            {
                ShowStatus("Login failed. Cannot retrieve JWT token from Server.", isError: true);
                if (splash != null)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                        splash.ShowAuthErrorAndResetOption();
                    });
                }
                return;
            }
        }
        catch (Exception ex)
        {
            ShowStatus($"Auth Error: {ex.Message}", isError: true);
            if (splash != null)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                    splash.ShowAuthErrorAndResetOption();
                });
            }
            return;
        }

        await LoadAssetsAsync(splash);
        
        if (splash != null)
        {
            splash.UpdateStatus(EZKPM.Client.Desktop.Resources.AppStrings.Startup_StatusStartClient, 100);
            await Task.Delay(500); // Artificial delay to ensure user sees 100%
            
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = this;
            }
            if (!Program.IsAutoStart)
            {
                this.Show();
            }
            splash.Close();
        }

        // Check Admin Status
        try 
        {
            var adminStatus = await _apiClient.GetAdminStatusAsync();
            if (adminStatus != null)
            {
                AdminPanelButton.IsVisible = adminStatus.HasAccessToAdminPanel;
                if (adminStatus.IsBootstrapActive)
                {
                    AdminPanelButton.Content = "🛡️ Admin Setup (Bootstrap)";
                }
            }
        }
        catch { }

        // Starte SSO SignalR Verbindung
        var userSid = EZKPM.Client.Desktop.Services.AdSearchService.GetCurrentUser()?.Sid ?? "S-1-5-21-DUMMY-TEST-USER";
        _ = _ssoSyncClient.ConnectAsync(EZKPM.Client.Desktop.Services.ConfigurationManager.CurrentConfig.ServerUrl, userSid);

        // Starte Auto-Type Watcher
        EZKPM.Client.Desktop.Services.AutoTypeWatcherService.StartWatching(
            () => _decryptedAssets.ToList(),
            async (matches, hWnd) => await OnAutoTypeMatchAsync(matches, hWnd)
        );
    }

    private async Task OnAutoTypeMatchAsync(List<EZKPM.Shared.Contracts.VaultAssetPayload> matches, IntPtr targetHwnd)
    {
        if (matches == null || matches.Count == 0) return;

        var prompt = new Views.AutoTypePromptWindow(matches);
        var result = await prompt.ShowDialog<EZKPM.Shared.Contracts.VaultAssetPayload>(this);
        
        if (result != null)
        {
            if (!Services.SessionManager.EnsureAuthenticated("Auto-Type ausführen")) return;

            string pattern = result.AutoType?.Pattern ?? "{USERNAME}{TAB}{PASSWORD}{ENTER}";
            int mode = result.AutoType?.Mode ?? 1;
            string username = result.Username ?? "";
            string password = result.Password ?? "";
            string title = result.Title ?? "";

            var clipboard = Avalonia.Controls.TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null) return;

            try
            {
                await EZKPM.Client.Desktop.Services.AutoTypeService.PerformAutoType(pattern, username, password, title, mode, clipboard, result.CustomFields);
            }
            catch (Exception ex)
            {
                ShowStatus($"Auto-Type Fehler: {ex.Message}", isError: true);
            }
        }
    }

    private async Task LoadAssetsAsync(Views.StartupWindow? splash = null)
    {
        try
        {
            var serverAssets = await _apiClient.GetAllAssetsAsync();
            _decryptedAssets.Clear();
            _failedDecryptionIds.Clear();

            int total = serverAssets.Count;
            int current = 0;
            foreach (var dto in serverAssets)
            {
                current++;
                if (splash != null && total > 0)
                {
                    double progress = 20.0 + (80.0 * ((double)current / total));
                    splash.UpdateStatus(string.Format(EZKPM.Client.Desktop.Resources.AppStrings.Startup_StatusDecryptAsset, current, total), progress);
                }

                try
                {
                    var payload = _cryptoService.DecryptAsset(dto);
                    if (payload != null) _decryptedAssets.Add(payload);
                }
                catch (Exception ex)
                {
                    // If decryption fails (wrong key, expired), track it so we can clean it up
                    Program.LogDebug($"Decryption failed for asset {dto.AssetId}: {ex.Message}");
                    _failedDecryptionIds.Add(dto.AssetId);
                }
            }
            // Ensure Private Folder exists
            string privateName = EZKPM.Client.Desktop.Resources.AppStrings.Main_PrivateFolder ?? "Private";
            bool hasPrivateFolder = _decryptedAssets.Any(a => a.AssetType == "Folder" && !a.IsDeleted && (a.Title == privateName || a.Title == "Privat" || a.Title == "Private"));
            
            if (!hasPrivateFolder)
            {
                var currentUser = EZKPM.Client.Desktop.Services.AdSearchService.GetCurrentUser()?.Sid ?? "S-1-5-21-DUMMY-TEST-USER";
                var newFolder = new VaultAssetPayload
                {
                    AssetType = "Folder",
                    Title = privateName,
                    IsInheriting = false,
                    Acls = new List<EZKPM.Shared.Contracts.AclEntryDto> { 
                        new EZKPM.Shared.Contracts.AclEntryDto { 
                            HashedSid = HashSid(currentUser), 
                            PermissionLevel = 3 
                        } 
                    },
                    TransientAssetId = Guid.NewGuid(),
                    ParentFolderId = null
                };
                
                try 
                {
                    var requestDto = _cryptoService.EncryptAsset(newFolder);
                    Guid realId = await _apiClient.CreateAssetAsync(requestDto);
                    newFolder.TransientAssetId = realId;
                    _decryptedAssets.Add(newFolder);
                }
                catch (Exception ex)
                {
                    Program.LogDebug($"Failed to create Private folder: {ex.Message}");
                }
            }

            // Ensure Environment Log Key exists (for encrypting client logs)
            await EnsureEnvironmentLogKeyAsync();

            BuildTree();
        }
        catch (Exception ex)
        {
            ShowStatus($"Load Error: {ex.Message}", isError: true);
        }
    }

    private async Task EnsureEnvironmentLogKeyAsync()
    {
        try
        {
            var existingPubKey = await _apiClient.GetEnvironmentPublicKeyAsync();
            if (!string.IsNullOrEmpty(existingPubKey))
            {
                return; // Key exists, nothing to do
            }

            // Key does not exist, generate an RSA key pair
            using var rsa = System.Security.Cryptography.RSA.Create(4096);
            var pubKeyBytes = rsa.ExportSubjectPublicKeyInfo();
            var privKeyBytes = rsa.ExportPkcs8PrivateKey();

            var pubKeyBase64 = Convert.ToBase64String(pubKeyBytes);
            var privKeyBase64 = Convert.ToBase64String(privKeyBytes);

            // Post public key to server
            bool success = await _apiClient.SetEnvironmentPublicKeyAsync(pubKeyBase64);
            if (success)
            {
                // Save private key as a VaultAsset
                var currentUser = EZKPM.Client.Desktop.Services.AdSearchService.GetCurrentUser()?.Sid ?? "S-1-5-21-DUMMY-TEST-USER";
                var newKeyAsset = new VaultAssetPayload
                {
                    AssetType = "SystemKey",
                    Title = "EnvironmentLogKey",
                    Username = "SYSTEM",
                    Password = privKeyBase64,
                    IsInheriting = false,
                    Acls = new List<EZKPM.Shared.Contracts.AclEntryDto> { 
                        new EZKPM.Shared.Contracts.AclEntryDto { 
                            HashedSid = HashSid(currentUser), 
                            PermissionLevel = 3 // Owner
                        } 
                    },
                    TransientAssetId = Guid.NewGuid(),
                    ParentFolderId = null
                };

                var requestDto = _cryptoService.EncryptAsset(newKeyAsset);
                Guid realId = await _apiClient.CreateAssetAsync(requestDto);
                newKeyAsset.TransientAssetId = realId;
                _decryptedAssets.Add(newKeyAsset);
            }
        }
        catch (Exception ex)
        {
            Program.LogDebug($"Failed to initialize Environment Log Key: {ex.Message}");
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
                node.Parent = parent;
                parent.Children.Add(node);
            }
            else
            {
                _treeNodes.Add(node);
            }
        }
        
        // Sort root nodes: Private first, then alphabetically
        string pName = EZKPM.Client.Desktop.Resources.AppStrings.Main_PrivateFolder ?? "Private";
        var sortedRoot = _treeNodes.OrderByDescending(n => n.Payload.Title == "Privat" || n.Payload.Title == "Private" || n.Payload.Title == pName)
                                   .ThenBy(n => n.Payload.Title).ToList();
        _treeNodes.Clear();
        foreach (var node in sortedRoot)
        {
            _treeNodes.Add(node);
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
        string selectedType = (TypeFilterComboBox?.SelectedItem as Avalonia.Controls.ComboBoxItem)?.Content?.ToString() ?? EZKPM.Client.Desktop.Resources.AppStrings.Main_FilterAll;

        bool isSearching = !string.IsNullOrWhiteSpace(searchText);

        var filtered = _decryptedAssets.AsEnumerable();

        if (selectedType == EZKPM.Client.Desktop.Resources.AppStrings.Main_FilterTrash)
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

            if (selectedType == EZKPM.Client.Desktop.Resources.AppStrings.Main_FilterExpired)
            {
                filtered = filtered.Where(a => a.IsExpired);
            }
            else if (selectedType != EZKPM.Client.Desktop.Resources.AppStrings.Main_FilterAll)
            {
                filtered = filtered.Where(a => a.AssetType == selectedType);
            }
            else if (!isSearching)
            {
                // In "Alle Typen" (ohne Suche) zeigen wir standardmäßig Assets UND Ordner des aktuellen Pfads.
                // Wenn wir allerdings NICHT im Root sind, wollen wir die Verzeichnisse sehen.
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
            string selectedType = (TypeFilterComboBox.SelectedItem as Avalonia.Controls.ComboBoxItem)?.Content?.ToString() ?? EZKPM.Client.Desktop.Resources.AppStrings.Main_FilterAll;
            RestoreSelectedAssetsButton.IsVisible = selectedType == EZKPM.Client.Desktop.Resources.AppStrings.Main_FilterTrash;
        }
    }

    private async void DeleteSelectedAssets_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (AssetsDataGrid.SelectedItems.Count == 0) return;

        var items = AssetsDataGrid.SelectedItems.Cast<VaultAssetPayload>().ToList();
        var isPapierkorb = (TypeFilterComboBox.SelectedItem as Avalonia.Controls.ComboBoxItem)?.Content?.ToString() == EZKPM.Client.Desktop.Resources.AppStrings.Main_FilterTrash;
        string msg = isPapierkorb 
            ? $"Möchten Sie {items.Count} Asset(s) wirklich unwiderruflich löschen?" 
            : $"Möchten Sie {items.Count} Asset(s) wirklich in den Papierkorb verschieben?";
            
        var dialog = new Views.ConfirmationDialog(msg);
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
                catch (UnauthorizedAccessException ex) when (ex.Message == "FORBIDDEN_NOT_OWNER")
                {
                    var overrideDialog = new Views.ConfirmationDialog($"Sie sind kein Owner von '{item.Title}'. Möchten Sie als Administrator das Asset trotzdem hart löschen (Datenmüll bereinigen)?");
                    var overrideResult = await overrideDialog.ShowDialogAsync(this);
                    if (overrideResult)
                    {
                        await _apiClient.DeleteAssetAsync(item.TransientAssetId.Value, forceAdmin: true);
                        count++;
                    }
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

    private async void CleanOrphans_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var dialog = new Views.ConfirmationDialog($"Möchten Sie wirklich alle herrenlosen Assets und {_failedDecryptionIds.Count} nicht mehr entschlüsselbare Assets (wegen verlorenem Schlüssel) endgültig löschen?");
        var result = await dialog.ShowDialogAsync(this);
        if (!result) return;

        try
        {
            int deleted = await _apiClient.CleanOrphanedAssetsAsync();
            int failedCount = 0;
            foreach (var id in _failedDecryptionIds)
            {
                try 
                {
                    await _apiClient.DeleteAssetAsync(id, true);
                    failedCount++;
                }
                catch { /* ignore individual failures */ }
            }
            
            ShowStatus($"Datenbankbereinigung abgeschlossen. {deleted} herrenlose(s) und {failedCount} defekte(s) Asset(s) entfernt.");
            await LoadAssetsAsync();
        }
        catch (Exception ex)
        {
            ShowStatus($"Fehler bei der Bereinigung: {ex.Message}", isError: true);
        }
    }

    private async void OpenRotationAssistant_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Services.SessionManager.EnsureAuthenticated("Rotation Assistant öffnen")) return;

        var assistant = new Views.RotationAssistantWindow(_decryptedAssets.ToList(), _cryptoService, _apiClient);
        await assistant.ShowDialog(this);
        
        // Reload after closing to reflect any rotated passwords
        await LoadAssetsAsync();
    }

    private async void OpenAdminPanel_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Services.SessionManager.EnsureAuthenticated("Admin Panel öffnen")) return;

        var adminDashboard = new Views.AdminDashboardWindow(_apiClient);
        await adminDashboard.ShowDialog(this);
        
        // Refresh admin button status after closing (in case bootstrap ended or rights changed)
        try 
        {
            var adminStatus = await _apiClient.GetAdminStatusAsync();
            if (adminStatus != null)
            {
                AdminPanelButton.IsVisible = adminStatus.HasAccessToAdminPanel;
                if (adminStatus.IsBootstrapActive)
                {
                    AdminPanelButton.Content = "🛡️ Admin Setup (Bootstrap)";
                }
                else
                {
                    AdminPanelButton.Content = "🛡️ Admin & Recovery";
                }
            }
        }
        catch { }
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
                    // Read headers first
                    using var headerStream = await file.OpenReadAsync();
                    
                    System.Text.Encoding encoding = System.Text.Encoding.UTF8;
                    try 
                    {
                        using var testReader = new System.IO.StreamReader(headerStream, new System.Text.UTF8Encoding(false, true), true, 1024, true);
                        await testReader.ReadToEndAsync();
                    }
                    catch (System.Text.DecoderFallbackException)
                    {
                        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
                        encoding = System.Text.Encoding.GetEncoding(1252);
                    }
                    headerStream.Position = 0;

                    using var reader = new System.IO.StreamReader(headerStream, encoding);
                    string headerLine = await reader.ReadLineAsync();
                    if (string.IsNullOrWhiteSpace(headerLine))
                    {
                        ShowStatus("Die CSV-Datei ist leer.");
                        return;
                    }
                    char delimiter = headerLine.Contains(';') ? ';' : ',';
                    var headers = headerLine.Split(delimiter).Select(h => h.Trim('"')).ToList();

                    // Open Mapping Window
                    var mappingWindow = new Views.CsvMappingWindow(headers);
                    await mappingWindow.ShowDialog(this);

                    if (!mappingWindow.IsConfirmed)
                    {
                        ShowStatus("Import abgebrochen.");
                        return;
                    }

                    var csvImporter = new CsvImporter();
                    csvImporter.Mapping = mappingWindow.Mapping;
                    importer = csvImporter;
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
                
                // Prompt for Target Folder
                var inputDialog = new Views.InputDialog("Ziel-Ordner für Import (Leer lassen für Root-Verzeichnis):", "Import " + DateTime.Now.ToString("yyyy-MM-dd"));
                string targetFolderName = await inputDialog.ShowDialogAsync(this);
                
                Guid? rootImportFolderId = null;
                if (!string.IsNullOrWhiteSpace(targetFolderName))
                {
                    // Create Root Folder
                    var rootFolderPayload = new VaultAssetPayload
                    {
                        Title = targetFolderName,
                        AssetType = "Folder",
                        Acls = new List<AclEntryDto> { new AclEntryDto { HashedSid = HashSid(currentUserSid), PermissionLevel = 3 } }
                    };
                    var rootRequestDto = _cryptoService.EncryptAsset(rootFolderPayload);
                    rootImportFolderId = await _apiClient.CreateAssetAsync(rootRequestDto);
                }

                foreach (var payload in importedPayloads)
                {
                    try
                    {
                        if (payload.ParentFolderId.HasValue && idMap.TryGetValue(payload.ParentFolderId.Value, out var realParentId))
                        {
                            payload.ParentFolderId = realParentId;
                        }
                        else if (!payload.ParentFolderId.HasValue && rootImportFolderId.HasValue)
                        {
                            // Assign root level imported items to our new Target Folder
                            payload.ParentFolderId = rootImportFolderId;
                        }

                        // Ensure we have owner ACLs, otherwise the server rejects it or it becomes invisible
                        if (payload.Acls.Count == 0)
                        {
                            payload.Acls.Add(new AclEntryDto { HashedSid = HashSid(currentUserSid), PermissionLevel = 3 });
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
                        Program.LogDebug($"Fehler beim Importieren eines Assets ({payload.Title}): {ex.Message}");
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
            if (payload.AssetType == "Folder")
            {
                // In den Ordner navigieren (analog Windows Explorer)
                var node = FindTreeNode(_treeNodes, payload.TransientAssetId.GetValueOrDefault());
                if (node != null)
                {
                    // Expand the folder itself
                    node.IsExpanded = true;
                    
                    // Expand all parents so it becomes visible
                    var parentNode = node.Parent;
                    while (parentNode != null)
                    {
                        parentNode.IsExpanded = true;
                        parentNode = parentNode.Parent;
                    }
                    
                    AssetTreeView.SelectedItem = node;
                    
                    // Clear the search box to exit search mode and show the folder's contents
                    if (SearchTextBox != null && !string.IsNullOrWhiteSpace(SearchTextBox.Text))
                    {
                        SearchTextBox.Text = ""; 
                    }
                }
            }
            else
            {
        var editor = new Views.AssetEditorWindow(payload, _decryptedAssets.ToList(), _cryptoService, _apiClient);
                editor.AssetSaved += async (s, args) => await LoadAssetsAsync();
                editor.Show();
            }
        }
    }

    private VaultTreeNode? FindTreeNode(IEnumerable<VaultTreeNode> nodes, Guid id)
    {
        foreach (var n in nodes)
        {
            if (n.Payload.TransientAssetId == id) return n;
            var child = FindTreeNode(n.Children, id);
            if (child != null) return child;
        }
        return null;
    }

    private void EditFolderMenuItem_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is VaultTreeNode node)
        {
            var editor = new Views.AssetEditorWindow(node.Payload, _decryptedAssets.ToList(), _cryptoService, _apiClient);
            editor.AssetSaved += async (s, ev) => await LoadAssetsAsync();
            editor.Show();
        }
    }

    private async void DeleteFolderMenuItem_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is VaultTreeNode node)
        {
            if (node.Children.Any())
            {
                ShowStatus("Ordner ist nicht leer und kann nicht gelöscht werden.", isError: true);
                return;
            }

            var dialog = new Views.ConfirmationDialog("Diesen Ordner wirklich löschen?");
            var result = await dialog.ShowDialogAsync(this);
            if (result)
            {
                try
                {
                    await _apiClient.DeleteAssetAsync(node.Payload.TransientAssetId.GetValueOrDefault());
                    ShowStatus("Ordner gelöscht.");
                    await LoadAssetsAsync();
                }
                catch (Exception ex)
                {
                    ShowStatus($"Fehler beim Löschen: {ex.Message}", isError: true);
                }
            }
        }
    }

    private async void ChangeServerMenuItem_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var configWindow = new Views.ServerConfigWindow(Services.ConfigurationManager.CurrentConfig.ServerUrl);
        await configWindow.ShowDialog(this);
        if (configWindow.IsConfirmed)
        {
            Services.ConfigurationManager.CurrentConfig.ServerUrl = configWindow.SelectedUrl;
            Services.ConfigurationManager.SaveConfig();
            
            // Re-instantiate the API client with the new URL
            var handler = new HttpClientHandler {  };
            var httpClient = new HttpClient(handler) { BaseAddress = new Uri(Services.ConfigurationManager.CurrentConfig.ServerUrl) };
            var newApiClient = new VaultApiClient(httpClient);
            
            // To properly apply the new API client to this window, we can just restart the application or swap it
            var dialog = new Views.ConfirmationDialog("Server erfolgreich geändert. Die App wird nun neu gestartet.");
            await dialog.ShowDialogAsync(this);
            
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                Environment.Exit(0);
            }
        }
    }

    private async void CheckForUpdates_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var updaterLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<Services.UpdaterService>.Instance;
        var updaterService = new Services.UpdaterService(updaterLogger);
        ShowStatus("Prüfe auf Updates...");
        await updaterService.CheckForUpdatesAsync(System.Threading.CancellationToken.None);
    }

    private void ShowVersion_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var version = (System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(System.Reflection.Assembly.GetExecutingAssembly())?.InformationalVersion ?? "1.0.0").Split('+')[0];
        ShowStatus($"EZKPM Vault Manager - Version {version}");
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
        var editor = new Views.AssetEditorWindow(payload, _decryptedAssets.ToList(), _cryptoService, _apiClient);
        editor.AssetSaved += async (s, args) => await LoadAssetsAsync();
        editor.Show();
    }
    private Guid? GetPrivateFolderId()
    {
        string pName = EZKPM.Client.Desktop.Resources.AppStrings.Main_PrivateFolder ?? "Private";
        var privateFolder = _decryptedAssets.FirstOrDefault(a => a.AssetType == "Folder" && !a.IsDeleted && (a.Title == "Privat" || a.Title == "Private" || a.Title == pName));
        return privateFolder?.TransientAssetId;
    }

    private void NewAssetButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedNode = AssetTreeView?.SelectedItem as VaultTreeNode;
        var payload = new VaultAssetPayload();
        if (selectedNode != null && selectedNode.Payload.AssetType == "Folder") {
            payload.ParentFolderId = selectedNode.Payload.TransientAssetId;
        } else {
            payload.ParentFolderId = GetPrivateFolderId();
        }
        var editor = new Views.AssetEditorWindow(payload, _decryptedAssets.ToList(), _cryptoService, _apiClient);
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
                
                // Auto-Inheritance on Move
                if (draggedNode.Payload.IsInheriting && targetNode.Payload.Acls != null)
                {
                    // Remove old inherited ACLs
                    draggedNode.Payload.Acls.RemoveAll(a => a.IsInherited);
                    
                    // Add new inherited ACLs from target
                    foreach (var pacl in targetNode.Payload.Acls)
                    {
                        if (!draggedNode.Payload.Acls.Any(a => a.HashedSid == pacl.HashedSid && !a.IsInherited))
                        {
                            draggedNode.Payload.Acls.Add(new AclEntryDto 
                            { 
                                HashedSid = pacl.HashedSid, 
                                DisplayName = pacl.DisplayName, 
                                PermissionLevel = pacl.PermissionLevel, 
                                IsInherited = true,
                                SourceGroupSid = pacl.SourceGroupSid,
                                SourceGroupName = pacl.SourceGroupName
                            });
                        }
                    }
                }

                try
                {
                    if (draggedNode.Payload.TransientAssetId.HasValue)
                    {
                        var requestDto = _cryptoService.EncryptAsset(draggedNode.Payload);
                        await _apiClient.UpdateAssetAsync(draggedNode.Payload.TransientAssetId.Value, requestDto);
                        
                        await LoadAssetsAsync();
                        ShowStatus($"'{draggedNode.Title}' in '{targetNode.Title}' verschoben (Rechte geerbt)!");
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
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            StatusTextBlock.Text = message;
            if (isError) StatusTextBlock.Foreground = Avalonia.Media.Brushes.Red;
            else if (isWarning) StatusTextBlock.Foreground = Avalonia.Media.Brushes.Orange;
            else StatusTextBlock.Foreground = Avalonia.Media.Brushes.Green;

            if (string.IsNullOrEmpty(message)) return;

            var level = isError ? "ERROR" : (isWarning ? "WARN" : "INFO");
            Program.LogDebug(message, level);
        });
    }

    private async void DataGridAutoType_Click(object sender, RoutedEventArgs e)
    {
        if (!Services.SessionManager.EnsureAuthenticated("Auto-Type ausführen")) return;

        if (sender is Button btn && btn.Tag is VaultAssetPayload payload)
        {
            // Hide window temporarily so focus goes back to the underlying app
            this.WindowState = WindowState.Minimized;
            
            string pattern = payload.AutoType?.Pattern ?? "{USERNAME}{TAB}{PASSWORD}{ENTER}";
            int mode = payload.AutoType?.Mode ?? 1;
            string username = payload.Username ?? "";
            string password = payload.Password ?? "";
            string title = payload.Title ?? "";

            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null) 
            {
                this.WindowState = WindowState.Normal;
                return;
            }

            try
            {
                await EZKPM.Client.Desktop.Services.AutoTypeService.PerformAutoType(pattern, username, password, title, mode, clipboard, payload.CustomFields);
            }
            catch (Exception ex)
            {
                ShowStatus($"Auto-Type Fehler: {ex.Message}", isError: true);
            }
            finally
            {
                this.WindowState = WindowState.Normal;
            }
        }
    }

}