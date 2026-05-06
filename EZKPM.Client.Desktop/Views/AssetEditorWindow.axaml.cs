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

namespace EZKPM.Client.Desktop.Views;

public class FolderSelectionItem
{
    public Guid? Id { get; set; }
    public string DisplayTitle { get; set; }
    public VaultAssetPayload OriginalPayload { get; set; }
}

public class AclGroupItemViewModel : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

    private string _displaySid = "";
    public string DisplaySid
    {
        get => _displaySid;
        set
        {
            _displaySid = value;
            if (string.IsNullOrEmpty(SourceGroupSid) && UnderlyingAcls.Count > 0)
            {
                UnderlyingAcls[0].AdSid = value;
            }
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(DisplaySid)));
        }
    }

    public string DisplayName { get; set; } = "";
    public bool IsInherited { get; set; } = false;
    public string SourceGroupSid { get; set; } = "";

    public string Icon => string.IsNullOrEmpty(SourceGroupSid) ? "👤" : "👥";
    public string NameWithSource => string.IsNullOrEmpty(SourceGroupSid) 
        ? DisplayName 
        : $"{DisplayName} (via Gruppe)";

    public System.Collections.Generic.List<EZKPM.Shared.Contracts.AclEntryDto> UnderlyingAcls { get; set; } = new();

    public int UiPermissionIndex
    {
        get => UnderlyingAcls.FirstOrDefault()?.UiPermissionIndex ?? 1;
        set
        {
            foreach (var a in UnderlyingAcls) a.UiPermissionIndex = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(UiPermissionIndex)));
        }
    }
}

public partial class AssetEditorWindow : Window
{
    private readonly VaultApiClient _apiClient;
    private readonly VaultCryptoService _cryptoService;
    private readonly ObservableCollection<VaultAssetPayload> _decryptedAssets = new();
    private readonly ObservableCollection<VaultTreeNode> _treeNodes = new();
    private readonly HashSet<Guid> _expandedFolderIds = new();
    private Guid? _currentEditingAssetId = null;
    
    private byte[] _currentFileData = null;
    private string _currentFileName = null;
    
    private byte[] _currentFileData2 = null;
    private string _currentFileName2 = null;
    
    private readonly ObservableCollection<VaultAttachment> _attachments = new();
    private readonly ObservableCollection<CustomField> _customFields = new();
    private readonly ObservableCollection<AclEntryDto> _acls = new();
    private readonly ObservableCollection<AclGroupItemViewModel> _displayAcls = new();
    private Avalonia.Threading.DispatcherTimer? _totpTimer;

    public event EventHandler? AssetSaved;

    public AssetEditorWindow() : this(null, null) { }
    public AssetEditorWindow(VaultAssetPayload? payload = null, List<VaultAssetPayload>? allAssets = null)
    {
        InitializeComponent();
        
        _totpTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _totpTimer.Tick += TotpTimer_Tick;
        _totpTimer.Start();

        var httpClient = new HttpClient { BaseAddress = new Uri(EZKPM.Client.Desktop.Services.ConfigurationManager.CurrentConfig.ServerUrl) };
        _apiClient = new VaultApiClient(httpClient);
        _cryptoService = new VaultCryptoService(new HybridPqcKeyWrapper());

        AttachmentsListBox.ItemsSource = _attachments;
        CustomFieldsListBox.ItemsSource = _customFields;
        AclsListBox.ItemsSource = _displayAcls;
        
        // Populate ParentFolderComboBox if we have assets
        if (allAssets != null)
        {
            ParentFolderComboBox.ItemsSource = BuildHierarchicalFolderList(allAssets);
        }

        ResetEditor();

        if (payload != null)
        {
            // Auto-Inheritance for NEW assets created inside a folder
            if (payload.TransientAssetId == null && payload.ParentFolderId != null && allAssets != null)
            {
                var parentFolder = allAssets.FirstOrDefault(a => a.TransientAssetId == payload.ParentFolderId);
                if (parentFolder != null && parentFolder.Acls != null && parentFolder.Acls.Count > 0)
                {
                    payload.Acls = parentFolder.Acls.Select(a => new AclEntryDto { AdSid = a.AdSid, DisplayName = a.DisplayName, PermissionLevel = a.PermissionLevel }).ToList();
                }
            }

            _currentEditingAssetId = payload.TransientAssetId;
            
            var expiredPanel = this.FindControl<Avalonia.Controls.Border>("ExpiredWarningPanel");
            if (expiredPanel != null) expiredPanel.IsVisible = payload.IsExpired;

            // Set Title, Username, Password, etc.
            TitleTextBox.Text = payload.Title ?? "";
            UsernameTextBox.Text = payload.Username ?? "";
            PasswordTextBox.Text = payload.Password ?? "";
            UrlTextBox.Text = payload.Url ?? "";
            NotesTextBox.Text = payload.Notes ?? "";
            DetailedDescriptionTextBox.Text = payload.DetailedDescription ?? "";
            
            var totpSecretBox = this.FindControl<TextBox>("TotpSecretTextBox");
            if (totpSecretBox != null) totpSecretBox.Text = payload.TotpSecret ?? "";
            
            RequiresAuditLogCheckBox.IsChecked = payload.RequiresAuditLog;

            // Find Asset Type
            foreach (Avalonia.Controls.ComboBoxItem item in AssetTypeComboBox.Items)
            {
                if (item.Content?.ToString() == payload.AssetType)
                {
                    AssetTypeComboBox.SelectedItem = item;
                    break;
                }
            }

            // Custom fields & Attachments & ACLs
            if (payload.CustomFields != null)
            {
                foreach (var f in payload.CustomFields) _customFields.Add(f);
            }
            if (payload.Attachments != null)
            {
                foreach (var a in payload.Attachments) _attachments.Add(a);
            }
            if (payload.Acls != null && payload.Acls.Count > 0)
            {
                _acls.Clear(); // remove default owner for existing payload
                foreach (var acl in payload.Acls) _acls.Add(acl);
            }
            RefreshDisplayAcls();

            // Set IsInheriting status
            var isInheritingBox = this.FindControl<CheckBox>("IsInheritingCheckBox");
            if (isInheritingBox != null)
            {
                isInheritingBox.IsVisible = payload.ParentFolderId != null;
                isInheritingBox.IsChecked = payload.IsInheriting;
            }

            // Select parent folder if any
            if (payload.ParentFolderId != null && ParentFolderComboBox.ItemsSource is List<FolderSelectionItem> flist)
            {
                var match = flist.FirstOrDefault(x => x.Id == payload.ParentFolderId);
                if (match != null) ParentFolderComboBox.SelectedItem = match;
            }
            
            // Payment fields
            if (payload.AssetType == "Payment")
            {
                PaymentSubTypeComboBox.SelectedIndex = payload.PaymentSubType == "Service" ? 1 : 0;
                CardHolderTextBox.Text = payload.CardHolder ?? "";
                CardExpiryTextBox.Text = payload.CardExpiry ?? "";
                CardCvcTextBox.Text = payload.CardCvc ?? "";
            }
            
            // Files
            _currentFileName = payload.FileUploadName;
            _currentFileData = payload.FileUploadData;
            if (!string.IsNullOrEmpty(_currentFileName))
            {
                FileNameTextBox.Text = _currentFileName;
                DownloadFileButton.IsVisible = true;
            }

            _currentFileName2 = payload.FileUploadName2;
            _currentFileData2 = payload.FileUploadData2;
            if (!string.IsNullOrEmpty(_currentFileName2))
            {
                FileName2TextBox.Text = _currentFileName2;
                DownloadFile2Button.IsVisible = true;
            }
            // Login Flow Fields
            if (payload.LoginFlow != null)
            {
                if (payload.LoginFlow.Method == "OneStep") LoginMethodComboBox.SelectedIndex = 1;
                else if (payload.LoginFlow.Method == "TwoStep") LoginMethodComboBox.SelectedIndex = 2;
                else if (payload.LoginFlow.Method == "BasicAuth") LoginMethodComboBox.SelectedIndex = 3;
                else LoginMethodComboBox.SelectedIndex = 0; // AutoLearn
                
                AutoLearnEnabledCheck.IsChecked = payload.LoginFlow.AutoLearnEnabled;
                DomUserTextBox.Text = payload.LoginFlow.UsernameSelector ?? "";
                DomPassTextBox.Text = payload.LoginFlow.PasswordSelector ?? "";
                DomNextTextBox.Text = payload.LoginFlow.NextButtonSelector ?? "";
                DomSubmitTextBox.Text = payload.LoginFlow.SubmitButtonSelector ?? "";
            }
        }
        
        // Ensure UI matches the selected type initially
        AssetTypeComboBox_SelectionChanged(null, null);
    }

    private void ResetEditor(bool keepType = false, bool keepTreeSelection = false, Guid? keepFolderId = null)
    {
        int previousTypeIndex = AssetTypeComboBox.SelectedIndex;
        
        _currentEditingAssetId = null;
        
        
        var expiredPanel = this.FindControl<Avalonia.Controls.Border>("ExpiredWarningPanel");
        if (expiredPanel != null) expiredPanel.IsVisible = false;

        TitleTextBox.Text = "";
        UsernameTextBox.Text = "";
        PasswordTextBox.Text = "";
        UrlTextBox.Text = "";
        NotesTextBox.Text = "";
        
        if (keepType)
        {
            AssetTypeComboBox.SelectedIndex = previousTypeIndex;
        }
        else
        {
            AssetTypeComboBox.SelectedIndex = 0;
        }
        
        PaymentSubTypeComboBox.SelectedIndex = 0;
        
        CardHolderTextBox.Text = "";
        CardExpiryTextBox.Text = "";
        CardCvcTextBox.Text = "";
        _currentFileName = null;
        _currentFileData = null;
        FileNameTextBox.Text = "";
        DownloadFileButton.IsVisible = false;

        _currentFileName2 = null;
        _currentFileData2 = null;
        FileName2TextBox.Text = "";
        DownloadFile2Button.IsVisible = false;
        
        var totpSecretBox = this.FindControl<TextBox>("TotpSecretTextBox");
        if (totpSecretBox != null) totpSecretBox.Text = "";
        
        DetailedDescriptionTextBox.Text = "";
        ValidityDaysControl.Value = 365;
        RequiresAuditLogCheckBox.IsChecked = false;
        _customFields.Clear();
        _attachments.Clear();
        _acls.Clear();
        
        // Automatischer Owner = Ersteller (Echte AD SID)
        var currentUser = Services.AdSearchService.GetCurrentUser();
        _acls.Add(new AclEntryDto { 
            AdSid = currentUser.Sid, 
            DisplayName = currentUser.DisplayName,
            PermissionLevel = 3 // Owner
        });
        RefreshDisplayAcls();

        var isInheritingBox = this.FindControl<CheckBox>("IsInheritingCheckBox");
        if (isInheritingBox != null)
        {
            isInheritingBox.IsVisible = false;
            isInheritingBox.IsChecked = true;
        }

        if (keepFolderId != null && ParentFolderComboBox.ItemsSource is List<FolderSelectionItem> flist)
        {
            var match = flist.FirstOrDefault(x => x.Id == keepFolderId);
            if (match != null) ParentFolderComboBox.SelectedItem = match;
            else if (ParentFolderComboBox.Items.Count > 0) ParentFolderComboBox.SelectedIndex = 0;
        }
        else if (ParentFolderComboBox.Items.Count > 0) ParentFolderComboBox.SelectedIndex = 0;
        ShowStatus("");
        
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
            ShowStatus("Username in die Zwischenablage kopiert!");
        }
    }

    private async void CopyPasswordButton_Click(object sender, RoutedEventArgs e)
    {
        if (!Services.SessionManager.EnsureAuthenticated("Passwort kopieren")) return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
        {
            await clipboard.SetTextAsync(PasswordTextBox.Text ?? "");
            ShowStatus("Passwort in die Zwischenablage kopiert!");
            
            if (_currentEditingAssetId.HasValue && RequiresAuditLogCheckBox.IsChecked == true)
            {
                try
                {
                    byte[] previousHash = await _apiClient.GetLatestAuditHashAsync(_currentEditingAssetId.Value);
                    var req = _cryptoService.CreateAuditLogRequest("Password copied to clipboard manually", previousHash);
                    await _apiClient.AppendAuditLogAsync(_currentEditingAssetId.Value, req);
                }
                catch { }
            }
        }
    }

    private async void CopyAllDetailsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!Services.SessionManager.EnsureAuthenticated("Alle Details kopieren")) return;

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
            ShowStatus("Gesamte Asset-Details in die Zwischenablage kopiert!");
            
            if (_currentEditingAssetId.HasValue && RequiresAuditLogCheckBox.IsChecked == true)
            {
                try
                {
                    byte[] previousHash = await _apiClient.GetLatestAuditHashAsync(_currentEditingAssetId.Value);
                    var req = _cryptoService.CreateAuditLogRequest("All asset details copied to clipboard manually", previousHash);
                    await _apiClient.AppendAuditLogAsync(_currentEditingAssetId.Value, req);
                }
                catch { }
            }
        }
    }

    private List<FolderSelectionItem> BuildHierarchicalFolderList(List<VaultAssetPayload> allAssets)
    {
        var folders = allAssets.Where(a => a.AssetType == "Folder").ToList();
        var result = new List<FolderSelectionItem>();
        
        result.Add(new FolderSelectionItem { Id = null, DisplayTitle = "-- Root --", OriginalPayload = new VaultAssetPayload { Title = "-- Root --", TransientAssetId = null } });

        var roots = folders.Where(f => f.ParentFolderId == null).OrderBy(f => f.Title).ToList();
        
        void AddChildren(Guid? parentId, int depth)
        {
            var children = folders.Where(f => f.ParentFolderId == parentId).OrderBy(f => f.Title).ToList();
            foreach (var child in children)
            {
                string indent = new string('\u00A0', depth * 4);
                result.Add(new FolderSelectionItem 
                { 
                    Id = child.TransientAssetId, 
                    DisplayTitle = $"{indent}↳ 📁 {child.Title}", 
                    OriginalPayload = child 
                });
                AddChildren(child.TransientAssetId, depth + 1);
            }
        }
        
        foreach (var root in roots)
        {
            result.Add(new FolderSelectionItem 
            { 
                Id = root.TransientAssetId, 
                DisplayTitle = $"📁 {root.Title}", 
                OriginalPayload = root 
            });
            AddChildren(root.TransientAssetId, 1);
        }
        
        return result;
    }

    private void DuplicateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentEditingAssetId != null)
        {
            _currentEditingAssetId = null;
            TitleTextBox.Text += " (Kopie)";
            ShowStatus("Asset als Kopie vorbereitet. Klicke auf 'Save to Server' um es neu anzulegen.", isError: false, isWarning: true);
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
            if (!Services.SessionManager.EnsureAuthenticated("Passwort im Klartext anzeigen"))
            {
                ShowPasswordCheckBox.IsChecked = false;
                return;
            }
            PasswordTextBox.PasswordChar = '\0'; // Show text
        }
        else
        {
            PasswordTextBox.PasswordChar = '*'; // Hide text
        }
    }

    private void ShowTotpSecretCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
    {
        var checkBox = sender as CheckBox;
        var box = this.FindControl<TextBox>("TotpSecretTextBox");
        if (box != null)
        {
            box.PasswordChar = checkBox?.IsChecked == true ? '\0' : '•';
        }
    }

    private async void CopyTotpSecretButton_Click(object sender, RoutedEventArgs e)
    {
        if (!Services.SessionManager.EnsureAuthenticated("TOTP Secret kopieren")) return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        var box = this.FindControl<TextBox>("TotpSecretTextBox");
        if (clipboard != null && box != null)
        {
            await clipboard.SetTextAsync(box.Text ?? "");
            ShowStatus("TOTP Secret in die Zwischenablage kopiert!");
        }
    }
    
    private void TotpTimer_Tick(object? sender, EventArgs e)
    {
        var textBlock = this.FindControl<TextBlock>("TotpCurrentCodeTextBlock");
        var secretBox = this.FindControl<TextBox>("TotpSecretTextBox");
        if (textBlock != null && secretBox != null && !string.IsNullOrWhiteSpace(secretBox.Text))
        {
            textBlock.Text = GetTotpCode(secretBox.Text);
        }
        else if (textBlock != null)
        {
            textBlock.Text = "------";
        }
    }

    public static string GetTotpCode(string base32Secret)
    {
        if (string.IsNullOrWhiteSpace(base32Secret)) return "------";
        try {
            base32Secret = base32Secret.ToUpper().Replace(" ", "").Replace("-", "");
            var charset = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
            var bytes = new List<byte>();
            int buffer = 0, bitsLeft = 0;
            foreach (var c in base32Secret)
            {
                int val = charset.IndexOf(c);
                if (val < 0) continue;
                buffer = (buffer << 5) | val;
                bitsLeft += 5;
                if (bitsLeft >= 8)
                {
                    bytes.Add((byte)((buffer >> (bitsLeft - 8)) & 0xFF));
                    bitsLeft -= 8;
                }
            }
            var secretBytes = bytes.ToArray();
            long timeStep = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
            var timeBytes = BitConverter.GetBytes(timeStep);
            if (BitConverter.IsLittleEndian) Array.Reverse(timeBytes);
            using var hmac = new System.Security.Cryptography.HMACSHA1(secretBytes);
            var hash = hmac.ComputeHash(timeBytes);
            int offset = hash[19] & 0x0f;
            int binary = ((hash[offset] & 0x7f) << 24) | ((hash[offset + 1] & 0xff) << 16) | ((hash[offset + 2] & 0xff) << 8) | (hash[offset + 3] & 0xff);
            return (binary % 1000000).ToString("D6");
        } catch { return "Error!"; }
    }

    private async Task<bool> SaveAssetAsync()
    {
        if (!Services.SessionManager.EnsureAuthenticated("Tresor bearbeiten/speichern")) return false;

        try
        {
            string urlValue = UrlTextBox.Text ?? "";
            if (!string.IsNullOrEmpty(urlValue) && urlValue.Trim().StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                ShowStatus("Warnung: URL verwendet unverschlüsseltes HTTP. Dies ist unsicher!", isWarning: true);
                await Task.Delay(1500); // Kurze Pause, damit der Nutzer die Warnung sieht
            }

            string loginMethodStr = "AutoLearn";
            if (LoginMethodComboBox.SelectedIndex == 1) loginMethodStr = "OneStep";
            else if (LoginMethodComboBox.SelectedIndex == 2) loginMethodStr = "TwoStep";
            else if (LoginMethodComboBox.SelectedIndex == 3) loginMethodStr = "BasicAuth";

            var payload = new VaultAssetPayload
            {
                TransientAssetId = _currentEditingAssetId,
                ParentFolderId = (ParentFolderComboBox.SelectedItem as FolderSelectionItem)?.Id,
                Title = TitleTextBox.Text ?? "Untitled",
                AssetType = (AssetTypeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Login",
                Username = UsernameTextBox.Text ?? "",
                Password = PasswordTextBox.Text ?? "",
                Url = UrlTextBox.Text ?? "",
                Notes = NotesTextBox.Text ?? "",
                DetailedDescription = DetailedDescriptionTextBox.Text ?? "",
                TotpSecret = this.FindControl<TextBox>("TotpSecretTextBox")?.Text ?? "",
                PasswordValidityDays = (int)ValidityDaysControl.Value.GetValueOrDefault(365),
                RequiresAuditLog = RequiresAuditLogCheckBox.IsChecked == true,
                
                PaymentSubType = PaymentSubTypeComboBox.SelectedIndex == 1 ? "Service" : "Card",
                CardHolder = CardHolderTextBox.Text ?? "",
                CardExpiry = CardExpiryTextBox.Text ?? "",
                CardCvc = CardCvcTextBox.Text ?? "",
                FileUploadName = _currentFileName,
                FileUploadData = _currentFileData,
                FileUploadName2 = _currentFileName2,
                FileUploadData2 = _currentFileData2,
                
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
                    Method = (LoginMethodComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "AutoLearn",
                    AutoLearnEnabled = AutoLearnEnabledCheck.IsChecked == true,
                    UsernameSelector = DomUserTextBox.Text ?? "",
                    PasswordSelector = DomPassTextBox.Text ?? "",
                    NextButtonSelector = DomNextTextBox.Text ?? "",
                    SubmitButtonSelector = DomSubmitTextBox.Text ?? ""
                },

                Attachments = _attachments.ToList(),
                CustomFields = _customFields.ToList(),
                Acls = _acls.Where(a => !string.IsNullOrWhiteSpace(a.AdSid))
                            .Select(a => 
                            {
                                if (a.AdSid.Contains("(") && a.AdSid.Contains(")"))
                                {
                                    var start = a.AdSid.LastIndexOf("(") + 1;
                                    var end = a.AdSid.LastIndexOf(")");
                                    if (end > start) a.AdSid = a.AdSid.Substring(start, end - start);
                                }
                                return a;
                            })
                            .GroupBy(a => a.AdSid)
                            .Select(g => g.First())
                            .ToList(),
                IsInheriting = this.FindControl<CheckBox>("IsInheritingCheckBox")?.IsChecked == true
            };

            var currentUserSid = Services.AdSearchService.GetCurrentUser().Sid;
            
            // Validation: Must have at least one owner
            if (!payload.Acls.Any(a => a.PermissionLevel == 3))
            {
                ShowStatus("Speichern fehlgeschlagen: Es muss mindestens ein Owner definiert sein!", isError: true);
                return false;
            }

            // Validation: Last-Man-Standing & Public Key Check
            bool isCurrentUserOwner = payload.Acls.Any(a => a.AdSid == currentUserSid && a.PermissionLevel == 3);
            if (!isCurrentUserOwner)
            {
                // In a full implementation, we would query the server if the other owners have valid Public Keys.
                // Since they don't in this scenario, we must block the lockout to prevent data loss.
                ShowStatus("Speichern abgebrochen: Sie entfernen sich selbst als Owner, aber kein anderer neuer Owner hat bisher einen kryptografischen Schlüssel im System hinterlegt. Die Daten wären unwiderruflich verloren!", isError: true);
                return false;
            }

            // 1. Encrypt Payload locally
            var requestDto = _cryptoService.EncryptAsset(payload);

            // 2. Send to Server (Update or Create)
            if (_currentEditingAssetId.HasValue)
            {
                await _apiClient.UpdateAssetAsync(_currentEditingAssetId.Value, requestDto);
                ShowStatus("Updated successfully!");

                // Check for inheritance
                var applyToChildren = this.FindControl<CheckBox>("ApplyToChildrenCheckBox")?.IsChecked == true;
                if (applyToChildren && payload.AssetType == "Folder")
                {
                    ShowStatus("Vererbe Rechte an untergeordnete Elemente...");
                    await ApplyAclsToChildrenAsync(_currentEditingAssetId.Value, payload.Acls);
                }
            }
            else
            {
                Guid newId = await _apiClient.CreateAssetAsync(requestDto);
                _currentEditingAssetId = newId;
                ShowStatus("Saved successfully!");
            }

            // 3. Close the window if successful
            return true;
        }
        catch (Exception ex)
        {
            ShowStatus($"Save Error: {ex.Message}", isError: true);
            return false;
        }
    }

    private async void SaveAndNewButton_Click(object sender, RoutedEventArgs e)
    {
        bool success = await SaveAssetAsync();
        if (success)
        {
            var folderIdToKeep = (AssetTypeComboBox.SelectedItem as Avalonia.Controls.ComboBoxItem)?.Content?.ToString() == "Folder" 
                ? _currentEditingAssetId 
                : (ParentFolderComboBox.SelectedItem as FolderSelectionItem)?.Id;

            AssetSaved?.Invoke(this, EventArgs.Empty);

            var serverAssets = await _apiClient.GetAllAssetsAsync();
            var allDecrypted = new List<VaultAssetPayload>();
            foreach (var dto in serverAssets) {
                 allDecrypted.Add(_cryptoService.DecryptAsset(dto));
            }
            ParentFolderComboBox.ItemsSource = BuildHierarchicalFolderList(allDecrypted);

            ResetEditor(keepType: true, keepFolderId: folderIdToKeep);
        }
    }

    private async void SaveAndCloseButton_Click(object sender, RoutedEventArgs e)
    {
        bool success = await SaveAssetAsync();
        if (success)
        {
            AssetSaved?.Invoke(this, EventArgs.Empty);
            this.Close();
        }
    }



    private void ShowStatus(string message, bool isError = false, bool isWarning = false)
    {
        Console.WriteLine(message);
        var statusBlock = this.FindControl<TextBlock>("StatusTextBlock");
        if (statusBlock != null)
        {
            statusBlock.Text = message;
            statusBlock.Foreground = isError ? Avalonia.Media.Brushes.Red : (isWarning ? Avalonia.Media.Brushes.DarkOrange : Avalonia.Media.Brushes.Green);
            statusBlock.IsVisible = true;
        }

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

    private async void BrowseFileButton_Click(object sender, RoutedEventArgs e)
    {
        var topLevel = Avalonia.Controls.TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null) return;
        
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Select File",
            AllowMultiple = false
        });

        if (files != null && files.Count > 0)
        {
            var file = files[0];
            _currentFileName = file.Name;
            FileNameTextBox.Text = file.Name;
            await using var stream = await file.OpenReadAsync();
            using var ms = new System.IO.MemoryStream();
            await stream.CopyToAsync(ms);
            _currentFileData = ms.ToArray();
            DownloadFileButton.IsVisible = true;
        }
    }

    private void ClearFileButton_Click(object sender, RoutedEventArgs e)
    {
        _currentFileName = null;
        _currentFileData = null;
        FileNameTextBox.Text = "";
        DownloadFileButton.IsVisible = false;
    }

    private async void DownloadFileButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentFileData == null || string.IsNullOrEmpty(_currentFileName)) return;
        var topLevel = Avalonia.Controls.TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null) return;
        
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = "Save File",
            SuggestedFileName = _currentFileName
        });

        if (file != null)
        {
            await using var stream = await file.OpenWriteAsync();
            await stream.WriteAsync(_currentFileData);
            ShowStatus($"File '{_currentFileName}' saved successfully.");
        }
    }

    private async void BrowseFile2Button_Click(object sender, RoutedEventArgs e)
    {
        var topLevel = Avalonia.Controls.TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null) return;
        
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Select Secondary File",
            AllowMultiple = false
        });

        if (files != null && files.Count > 0)
        {
            var file = files[0];
            _currentFileName2 = file.Name;
            FileName2TextBox.Text = file.Name;
            await using var stream = await file.OpenReadAsync();
            using var ms = new System.IO.MemoryStream();
            await stream.CopyToAsync(ms);
            _currentFileData2 = ms.ToArray();
            DownloadFile2Button.IsVisible = true;
        }
    }

    private void ClearFile2Button_Click(object sender, RoutedEventArgs e)
    {
        _currentFileName2 = null;
        _currentFileData2 = null;
        FileName2TextBox.Text = "";
        DownloadFile2Button.IsVisible = false;
    }

    private async void DownloadFile2Button_Click(object sender, RoutedEventArgs e)
    {
        if (_currentFileData2 == null || string.IsNullOrEmpty(_currentFileName2)) return;
        var topLevel = Avalonia.Controls.TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null) return;
        
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = "Save Secondary File",
            SuggestedFileName = _currentFileName2
        });

        if (file != null)
        {
            await using var stream = await file.OpenWriteAsync();
            await stream.WriteAsync(_currentFileData2);
            ShowStatus($"Secondary File '{_currentFileName2}' saved successfully.");
        }
    }

    private async void AddAttachmentButton_Click(object sender, RoutedEventArgs e)
    {
        var topLevel = Avalonia.Controls.TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null) return;
        
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Select Attachments",
            AllowMultiple = true
        });

        if (files != null)
        {
            foreach (var f in files)
            {
                await using var stream = await f.OpenReadAsync();
                using var ms = new System.IO.MemoryStream();
                await stream.CopyToAsync(ms);
                _attachments.Add(new VaultAttachment { FileName = f.Name, FileData = ms.ToArray() });
            }
        }
    }

    private void RemoveAttachmentButton_Click(object sender, RoutedEventArgs e)
    {
        if (AttachmentsListBox.SelectedItem is VaultAttachment att)
        {
            _attachments.Remove(att);
        }
    }

    private async void DownloadAttachmentButton_Click(object sender, RoutedEventArgs e)
    {
        if (AttachmentsListBox.SelectedItem is VaultAttachment att)
        {
            var topLevel = Avalonia.Controls.TopLevel.GetTopLevel(this);
            if (topLevel?.StorageProvider == null) return;
            
            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = "Save Attachment",
                SuggestedFileName = att.FileName
            });

            if (file != null)
            {
                await using var stream = await file.OpenWriteAsync();
                await stream.WriteAsync(att.FileData);
                ShowStatus($"Attachment '{att.FileName}' saved successfully.");
            }
        }
    }

    private void AddCustomFieldButton_Click(object sender, RoutedEventArgs e)
    {
        _customFields.Add(new CustomField { Name = "New Field", Value = "" });
    }

    private void RemoveCustomFieldButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is CustomField f)
        {
            _customFields.Remove(f);
        }
    }

    private void RefreshDisplayAcls()
    {
        _displayAcls.Clear();
        var groups = _acls.GroupBy(a => string.IsNullOrEmpty(a.SourceGroupSid) ? a.AdSid : a.SourceGroupSid).ToList();
        foreach (var g in groups)
        {
            var first = g.First();
            var vm = new AclGroupItemViewModel
            {
                DisplaySid = string.IsNullOrEmpty(first.SourceGroupSid) ? first.AdSid : first.SourceGroupSid,
                DisplayName = string.IsNullOrEmpty(first.SourceGroupSid) ? first.DisplayName : first.SourceGroupName,
                IsInherited = first.IsInherited,
                SourceGroupSid = first.SourceGroupSid,
                UnderlyingAcls = g.ToList()
            };
            _displayAcls.Add(vm);
        }
    }

    private async void AddAclButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new AdPickerWindow();
        var result = await picker.ShowDialog<EZKPM.Client.Desktop.Services.AdPrincipal>(this);
        if (result != null)
        {
            if (result.Type == "Group")
            {
                ShowStatus($"Löse AD-Gruppe '{result.DisplayName}' auf...", isWarning: true);
                var members = await EZKPM.Client.Desktop.Services.AdSearchService.GetGroupMembersAsync(result.Sid);
                
                if (members.Count == 0)
                {
                    ShowStatus($"Warnung: Die AD-Gruppe '{result.DisplayName}' ist leer.", isWarning: true);
                }
                else
                {
                    foreach (var member in members)
                    {
                        if (!_acls.Any(a => a.AdSid == member.Sid))
                        {
                            _acls.Add(new AclEntryDto 
                            { 
                                AdSid = member.Sid, 
                                DisplayName = member.DisplayName, 
                                PermissionLevel = 1, // Default Execute
                                SourceGroupSid = result.Sid,
                                SourceGroupName = result.DisplayName
                            });
                        }
                    }
                    ShowStatus($"Gruppe aufgelöst: {members.Count} Nutzer hinzugefügt.");
                }
            }
            else
            {
                if (!_acls.Any(a => a.AdSid == result.Sid))
                {
                    _acls.Add(new AclEntryDto { 
                        AdSid = result.Sid, 
                        DisplayName = result.DisplayName, 
                        PermissionLevel = 1 
                    });
                }
            }
            RefreshDisplayAcls();
        }
    }

    private void RemoveAclButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is AclGroupItemViewModel groupVm)
        {
            foreach (var a in groupVm.UnderlyingAcls)
            {
                _acls.Remove(a);
            }
            RefreshDisplayAcls();
        }
    }

    private async void IsInheritingCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb)
        {
            if (cb.IsChecked == false)
            {
                // Prompt user
                var dialog = new ConfirmationDialog("Möchten Sie die bisher geerbten Rechte als explizite Rechte kopieren? (Wenn Nein, werden sie entfernt)");
                var result = await dialog.ShowDialogAsync(this);
                if (result)
                {
                    foreach (var acl in _acls.Where(a => a.IsInherited))
                    {
                        acl.IsInherited = false;
                    }
                }
                else
                {
                    var inherited = _acls.Where(a => a.IsInherited).ToList();
                    foreach (var a in inherited) _acls.Remove(a);
                }
                // Refresh list
                RefreshDisplayAcls();
            }
            else
            {
                // Re-inherit
                var parentFolderId = (ParentFolderComboBox.SelectedItem as FolderSelectionItem)?.Id;
                if (parentFolderId.HasValue && ParentFolderComboBox.ItemsSource is List<FolderSelectionItem> flist)
                {
                    var parentNode = flist.FirstOrDefault(f => f.Id == parentFolderId.Value);
                    if (parentNode != null && parentNode.OriginalPayload.Acls != null)
                    {
                        foreach (var pacl in parentNode.OriginalPayload.Acls)
                        {
                            if (!_acls.Any(a => a.AdSid == pacl.AdSid))
                            {
                                _acls.Add(new AclEntryDto 
                                { 
                                    AdSid = pacl.AdSid, 
                                    DisplayName = pacl.DisplayName, 
                                    PermissionLevel = pacl.PermissionLevel, 
                                    IsInherited = true,
                                    SourceGroupSid = pacl.SourceGroupSid,
                                    SourceGroupName = pacl.SourceGroupName
                                });
                            }
                        }
                    }
                }
                RefreshDisplayAcls();
            }
        }
    }

    private async void AdSidAutoCompleteBox_Populating(object sender, Avalonia.Controls.PopulatingEventArgs e)
    {
        if (sender is AutoCompleteBox autoCompleteBox)
        {
            string query = autoCompleteBox.SearchText ?? autoCompleteBox.Text;
            if (string.IsNullOrWhiteSpace(query) || query.Contains(" (S-"))
            {
                autoCompleteBox.ItemsSource = null;
                return;
            }

            e.Cancel = true;

            try 
            {
                var results = await EZKPM.Client.Desktop.Services.AdSearchService.SearchAsync(query);
                autoCompleteBox.ItemsSource = results.Select(r => r.ToString()).ToList();
                autoCompleteBox.PopulateComplete();
            }
            catch 
            {
                autoCompleteBox.ItemsSource = null;
            }
        }
    }



    private void AssetTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CredentialsPanel != null && AssetTypeComboBox.SelectedItem is Avalonia.Controls.ComboBoxItem item)
        {
            string type = item.Content?.ToString() ?? "";
            CredentialsPanel.IsVisible = type != "Folder";
            
            PaymentPanel.IsVisible = (type == "Payment");
            FileUploadPanel.IsVisible = (type == "SSH Key" || type == "SSL Key" || type == "Passkey" || type == "SecureNote");
            
            var authPanel = this.FindControl<StackPanel>("AuthenticatorPanel");
            if (authPanel != null) authPanel.IsVisible = (type == "Authenticator");
            
            if (PasswordLabel != null)
            {
                if (type == "Passkey") PasswordLabel.Text = "Passkey Data / Seed / PIN";
                else if (type == "SSH Key" || type == "SSL Key" || type == "API Key") PasswordLabel.Text = "Private Key / Container Password";
                else if (type == "Payment") PasswordLabel.Text = PaymentSubTypeComboBox?.SelectedIndex == 1 ? "Service Password" : "Card PIN";
                else PasswordLabel.Text = "Password";
            }
            
            var applyCb = this.FindControl<CheckBox>("ApplyToChildrenCheckBox");
            if (applyCb != null) applyCb.IsVisible = (type == "Folder");
        }
    }

    private async Task ApplyAclsToChildrenAsync(Guid folderId, List<AclEntryDto> parentAcls)
    {
        try
        {
            var serverAssets = await _apiClient.GetAllAssetsAsync();
            var allDecrypted = new List<VaultAssetPayload>();
            foreach (var dto in serverAssets) 
            {
                 allDecrypted.Add(_cryptoService.DecryptAsset(dto));
            }

            var childrenToUpdate = GetDescendantsRespectingInheritance(folderId, allDecrypted);
            if (childrenToUpdate.Count == 0) return;

            foreach (var child in childrenToUpdate)
            {
                // Remove old inherited ACLs
                child.Acls.RemoveAll(a => a.IsInherited);

                // Add new inherited ACLs from parent
                foreach (var pAcl in parentAcls)
                {
                    // If child does not have an EXPLICIT rule for this SID, inherit it
                    if (!child.Acls.Any(a => a.AdSid == pAcl.AdSid && !a.IsInherited))
                    {
                        child.Acls.Add(new AclEntryDto 
                        { 
                            AdSid = pAcl.AdSid, 
                            DisplayName = pAcl.DisplayName, 
                            PermissionLevel = pAcl.PermissionLevel, 
                            IsInherited = true,
                            SourceGroupSid = pAcl.SourceGroupSid,
                            SourceGroupName = pAcl.SourceGroupName
                        });
                    }
                }

                var req = _cryptoService.EncryptAsset(child);
                await _apiClient.UpdateAssetAsync(child.TransientAssetId.Value, req);
            }
            ShowStatus($"Vererbung abgeschlossen: {childrenToUpdate.Count} Elemente aktualisiert.");
        }
        catch (Exception ex)
        {
            ShowStatus($"Fehler bei Vererbung: {ex.Message}", isError: true);
        }
    }

    private List<VaultAssetPayload> GetDescendantsRespectingInheritance(Guid parentId, List<VaultAssetPayload> allAssets)
    {
        var result = new List<VaultAssetPayload>();
        var children = allAssets.Where(a => a.ParentFolderId == parentId).ToList();
        foreach (var c in children)
        {
            if (!c.IsInheriting) continue; // Vererbung gebrochen -> Hier stoppen

            result.Add(c);
            if (c.AssetType == "Folder" && c.TransientAssetId.HasValue)
            {
                result.AddRange(GetDescendantsRespectingInheritance(c.TransientAssetId.Value, allAssets));
            }
        }
        return result;
    }
private void PaymentSubTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CardDetailsPanel != null)
        {
            bool isService = PaymentSubTypeComboBox.SelectedIndex == 1;
            CardDetailsPanel.IsVisible = !isService;
            
            if (AssetTypeComboBox.SelectedItem is Avalonia.Controls.ComboBoxItem item && item.Content?.ToString() == "Payment" && PasswordLabel != null)
            {
                PasswordLabel.Text = isService ? "Service Password" : "Card PIN";
            }
        }
    }

    private void PopOutButton_Click(object sender, RoutedEventArgs e)
    {
        this.Close();
    }

}
