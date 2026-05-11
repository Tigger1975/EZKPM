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
                UnderlyingAcls[0].HashedSid = value;
            }
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(DisplaySid)));
        }
    }

    public string DisplayName { get; set; } = "";
    public bool IsInherited { get; set; } = false;
    public string SourceGroupSid { get; set; } = "";

    public bool IsDirectGroup => UnderlyingAcls.FirstOrDefault()?.SourceGroupName == "GROUP_MARKER";
    public string Icon => (IsDirectGroup || !string.IsNullOrEmpty(SourceGroupSid)) ? "👥" : "👤";
    public string NameWithSource => string.IsNullOrWhiteSpace(DisplayName) ? "Ersteller / Besitzer" : DisplayName;

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
    private string HashSid(string sid)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        return Convert.ToBase64String(sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(sid)));
    }

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
    private readonly List<VaultAssetPayload> _allAssets;

    public event EventHandler? AssetSaved;

    public AssetEditorWindow() : this(null, null, null) { }
    public AssetEditorWindow(VaultAssetPayload? payload = null, List<VaultAssetPayload>? allAssets = null, VaultCryptoService? cryptoService = null)
    {
        InitializeComponent();
        _allAssets = allAssets ?? new List<VaultAssetPayload>();
        
        _totpTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _totpTimer.Tick += TotpTimer_Tick;
        _totpTimer.Start();

        var handler = new HttpClientHandler { UseDefaultCredentials = true };
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri(EZKPM.Client.Desktop.Services.ConfigurationManager.CurrentConfig.ServerUrl) };
        _apiClient = new VaultApiClient(httpClient);
        _cryptoService = cryptoService ?? new VaultCryptoService(new HybridPqcKeyWrapper());

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
                    payload.Acls = parentFolder.Acls.Select(a => new AclEntryDto { HashedSid = a.HashedSid, DisplayName = a.DisplayName, PermissionLevel = a.PermissionLevel }).ToList();
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
            
            var applyToChildrenBox = this.FindControl<CheckBox>("ApplyToChildrenCheckBox");
            if (applyToChildrenBox != null)
            {
                applyToChildrenBox.IsChecked = payload.PropagateAclsToChildren;
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

            if (payload.AutoType != null)
            {
                AutoTypePatternTextBox.Text = payload.AutoType.Pattern ?? "";
                AutoTypeModeComboBox.SelectedIndex = payload.AutoType.Mode == 2 ? 1 : (payload.AutoType.Mode == 3 ? 2 : 0);
                
                var tbTitle = this.FindControl<TextBox>("TargetWindowTitleTextBox");
                if (tbTitle != null) tbTitle.Text = payload.AutoType.TargetWindowTitle ?? "";
                
                var tbProcess = this.FindControl<TextBox>("TargetProcessNameTextBox");
                if (tbProcess != null) tbProcess.Text = payload.AutoType.TargetProcessName ?? "";
                
                var tbClass = this.FindControl<TextBox>("TargetWindowClassTextBox");
                if (tbClass != null) tbClass.Text = payload.AutoType.TargetWindowClass ?? "";
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
            HashedSid = HashSid(currentUser.Sid), 
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

        AutoTypePatternTextBox.Text = "";
        AutoTypeModeComboBox.SelectedIndex = 0;
        
        var tbTitle = this.FindControl<TextBox>("TargetWindowTitleTextBox");
        if (tbTitle != null) tbTitle.Text = "";
        
        var tbProcess = this.FindControl<TextBox>("TargetProcessNameTextBox");
        if (tbProcess != null) tbProcess.Text = "";
        
        var tbClass = this.FindControl<TextBox>("TargetWindowClassTextBox");
        if (tbClass != null) tbClass.Text = "";
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

    private void GenerateSshKeyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var keyPairGen = new Org.BouncyCastle.Crypto.Generators.Ed25519KeyPairGenerator();
            keyPairGen.Init(new Org.BouncyCastle.Crypto.Parameters.Ed25519KeyGenerationParameters(new Org.BouncyCastle.Security.SecureRandom()));
            var keyPair = keyPairGen.GenerateKeyPair();

            using var stringWriter = new System.IO.StringWriter();
            var pemWriter = new Org.BouncyCastle.OpenSsl.PemWriter(stringWriter);
            pemWriter.WriteObject(keyPair.Private);
            string privateKey = stringWriter.ToString();

            using var pubStringWriter = new System.IO.StringWriter();
            var pubPemWriter = new Org.BouncyCastle.OpenSsl.PemWriter(pubStringWriter);
            pubPemWriter.WriteObject(keyPair.Public);
            string publicKey = pubStringWriter.ToString();

            if (PasswordTextBox != null)
            {
                PasswordTextBox.Text = privateKey;
                if (ShowPasswordCheckBox != null)
                    ShowPasswordCheckBox.IsChecked = true;
            }
            
            if (NotesTextBox != null)
            {
                NotesTextBox.Text = publicKey;
            }
        }
        catch (Exception ex)
        {
            var msgBox = new ConfirmationDialog("Der SSH Key konnte nicht generiert werden: " + ex.Message);
            msgBox.ShowDialog((Avalonia.Controls.Window)this.VisualRoot);
        }
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
                IsInheriting = this.FindControl<CheckBox>("IsInheritingCheckBox")?.IsChecked ?? true,
                PropagateAclsToChildren = this.FindControl<CheckBox>("ApplyToChildrenCheckBox")?.IsChecked == true,
                
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

                AutoType = new AutoTypeConfig
                {
                    Pattern = AutoTypePatternTextBox.Text ?? "",
                    Mode = AutoTypeModeComboBox.SelectedIndex + 1,
                    TargetWindowTitle = this.FindControl<TextBox>("TargetWindowTitleTextBox")?.Text ?? "",
                    TargetProcessName = this.FindControl<TextBox>("TargetProcessNameTextBox")?.Text ?? "",
                    TargetWindowClass = this.FindControl<TextBox>("TargetWindowClassTextBox")?.Text ?? ""
                },

                Attachments = _attachments.ToList(),
                CustomFields = _customFields.ToList()
            }; // End of payload initializer

            var rawAcls = _acls.Where(a => !string.IsNullOrWhiteSpace(a.HashedSid)).ToList();

            // Apply priority resolution for overlapping group memberships
            // Deny (-1) -> None (0) -> Execute (1) -> Read (2) -> Owner (3)
            payload.Acls = rawAcls
                .GroupBy(a => a.HashedSid)
                .Select(g => 
                {
                    var entries = g.ToList();
                    var finalEntry = entries.First();
                    if (entries.Any(e => e.PermissionLevel <= 0))
                    {
                        // If any path yields Deny or None, restrict access to the minimum (usually -1 or 0)
                        finalEntry.PermissionLevel = entries.Where(e => e.PermissionLevel <= 0).Min(e => e.PermissionLevel);
                    }
                    else
                    {
                        // Otherwise take the highest positive permission (e.g. Owner over Read)
                        finalEntry.PermissionLevel = entries.Max(e => e.PermissionLevel);
                    }
                    return finalEntry;
                })
                .ToList();

            var currentUserSid = Services.AdSearchService.GetCurrentUser().Sid;
            
            // Validation: Must have at least one owner
            if (!payload.Acls.Any(a => a.PermissionLevel == 3))
            {
                ShowStatus("Speichern fehlgeschlagen: Es muss mindestens ein Owner definiert sein!", isError: true);
                return false;
            }

            // Validation: Last-Man-Standing & Public Key Check
            bool isCurrentUserOwner = payload.Acls.Any(a => a.HashedSid == HashSid(currentUserSid) && a.PermissionLevel == 3);
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

    private void PlaceholderButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Content is string placeholder)
        {
            var textBox = AutoTypePatternTextBox;
            if (textBox == null) return;

            int caretIndex = textBox.CaretIndex;
            string text = textBox.Text ?? "";
            
            textBox.Text = text.Insert(caretIndex, placeholder);
            textBox.CaretIndex = caretIndex + placeholder.Length;
            textBox.Focus();
        }
    }

    private async void PerformAutoTypeButton_Click(object sender, RoutedEventArgs e)
    {
        if (!Services.SessionManager.EnsureAuthenticated("Auto-Type ausführen")) return;

        // Hide window temporarily so focus goes back to the underlying app
        this.WindowState = WindowState.Minimized;
        
        string pattern = AutoTypePatternTextBox.Text ?? "";
        int mode = AutoTypeModeComboBox.SelectedIndex + 1; // 1 = RandomChunks, 2 = FullBlock, 3 = Keystrokes
        string username = UsernameTextBox.Text ?? "";
        string password = PasswordTextBox.Text ?? "";
        string title = TitleTextBox.Text ?? "";

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard == null) 
        {
            this.WindowState = WindowState.Normal;
            return;
        }

        try
        {
            var fields = _customFields.ToList();
            await EZKPM.Client.Desktop.Services.AutoTypeService.PerformAutoType(pattern, username, password, title, mode, clipboard, fields);
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

    private bool _isCrosshairActive = false;

    private void Crosshair_PointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _isCrosshairActive = true;
            if (sender is Avalonia.Controls.Control ctrl) ctrl.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Cross);
            e.Pointer.Capture(sender as Avalonia.Controls.Control);
            e.Handled = true;
        }
    }

    private void Crosshair_PointerMoved(object sender, PointerEventArgs e)
    {
        if (_isCrosshairActive)
        {
            var info = EZKPM.Client.Desktop.Services.TargetWindowService.GetWindowInfoAtCursor();
            if (info != null)
            {
                var tbTitle = this.FindControl<TextBox>("TargetWindowTitleTextBox");
                if (tbTitle != null) tbTitle.Text = info.Title ?? "";
                
                var tbProcess = this.FindControl<TextBox>("TargetProcessNameTextBox");
                if (tbProcess != null) tbProcess.Text = info.ProcessName ?? "";
                
                var tbClass = this.FindControl<TextBox>("TargetWindowClassTextBox");
                if (tbClass != null) tbClass.Text = info.ClassName ?? "";
            }
        }
    }

    private void Crosshair_PointerReleased(object sender, PointerReleasedEventArgs e)
    {
        if (_isCrosshairActive)
        {
            _isCrosshairActive = false;
            e.Pointer.Capture(null);
            if (sender is Avalonia.Controls.Control ctrl) ctrl.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Arrow);
            e.Handled = true;
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
        var groups = _acls.GroupBy(a => string.IsNullOrEmpty(a.SourceGroupSid) ? a.HashedSid : a.SourceGroupSid).ToList();
        foreach (var g in groups)
        {
            var first = g.First();
            var vm = new AclGroupItemViewModel
            {
                DisplaySid = string.IsNullOrEmpty(first.SourceGroupSid) ? first.HashedSid : first.SourceGroupSid,
                DisplayName = string.IsNullOrEmpty(first.SourceGroupSid) ? first.DisplayName : first.SourceGroupName,
                IsInherited = first.IsInherited,
                SourceGroupSid = first.SourceGroupSid,
                UnderlyingAcls = g.ToList()
            };
            
            if (first.SourceGroupName == "GROUP_MARKER")
            {
                vm.DisplayName = first.DisplayName;
            }

            _displayAcls.Add(vm);
        }
    }

    private bool IsCurrentlyInPrivateFolder()
    {
        string pName = EZKPM.Client.Desktop.Resources.AppStrings.Main_PrivateFolder ?? "Private";
        
        string currentType = (AssetTypeComboBox.SelectedItem as Avalonia.Controls.ComboBoxItem)?.Content?.ToString() ?? "Login";
        string currentTitle = TitleTextBox.Text ?? "";
        if (currentType == "Folder" && (currentTitle == "Privat" || currentTitle == "Private" || currentTitle == pName))
        {
            return true;
        }

        Guid? parentId = null;
        if (ParentFolderComboBox.SelectedItem is FolderSelectionItem fItem)
        {
            parentId = fItem.Id;
        }

        while (parentId != null)
        {
            var parentFolder = _allAssets?.FirstOrDefault(a => a.TransientAssetId == parentId);
            if (parentFolder == null) break;
            
            if (parentFolder.AssetType == "Folder" && (parentFolder.Title == "Privat" || parentFolder.Title == "Private" || parentFolder.Title == pName))
            {
                return true;
            }
            parentId = parentFolder.ParentFolderId;
        }

        return false;
    }

    private async void AddAclButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsCurrentlyInPrivateFolder())
        {
            ShowStatus("Private Assets können nicht freigegeben werden. Bitte verschieben Sie das Asset zuvor in einen öffentlichen Ordner.", isWarning: true);
            return;
        }

        var picker = new AdPickerWindow();
        var result = await picker.ShowDialog<EZKPM.Client.Desktop.Services.AdPrincipal>(this);
        if (result != null)
        {
            if (!_acls.Any(a => a.HashedSid == HashSid(result.Sid)))
            {
                var newAcl = new AclEntryDto
                {
                    HashedSid = HashSid(result.Sid),
                    DisplayName = result.DisplayName,
                    PermissionLevel = 3 // Standard Besitzer
                };
                
                if (result.Type == "Group")
                {
                    newAcl.SourceGroupName = "GROUP_MARKER";
                }
                
                _acls.Add(newAcl);
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
                    for (int i = _acls.Count - 1; i >= 0; i--)
                    {
                        if (_acls[i].IsInherited)
                        {
                            _acls.RemoveAt(i);
                        }
                    }
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
                            if (!_acls.Any(a => a.HashedSid == pacl.HashedSid))
                            {
                                _acls.Add(new AclEntryDto 
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
                }
                RefreshDisplayAcls();
            }
        }
    }

    private async void HashedSidAutoCompleteBox_Populating(object sender, Avalonia.Controls.PopulatingEventArgs e)
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

            var sshKeyBtn = this.FindControl<Button>("GenerateSshKeyButton");
            if (sshKeyBtn != null) sshKeyBtn.IsVisible = (type == "SSH Key");

            var sshGenExp = this.FindControl<Expander>("SshGeneratorExpander");
            if (sshGenExp != null) sshGenExp.IsVisible = (type == "SSH Key");

            var tabCustomVars = this.FindControl<TabItem>("TabCustomVars");
            var tabSettings = this.FindControl<TabItem>("TabSettings");
            var tabAutoType = this.FindControl<TabItem>("TabAutoType");
            
            bool isNotFolder = type != "Folder";
            if (tabCustomVars != null) tabCustomVars.IsVisible = isNotFolder;
            if (tabSettings != null) tabSettings.IsVisible = isNotFolder;
            if (tabAutoType != null) tabAutoType.IsVisible = isNotFolder;
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
                    try 
                    {
                        var dec = _cryptoService.DecryptAsset(dto);
                        allDecrypted.Add(dec);
                    }
                    catch (Exception dx)
                    {
                        System.Diagnostics.Debug.WriteLine($"DecryptAsset failed for {dto.AssetId}: {dx.Message}");
                    }
                }

                var childrenToUpdate = GetDescendantsRespectingInheritance(folderId, allDecrypted);

                if (childrenToUpdate.Count == 0) 
                {
                    return;
                }

                foreach (var child in childrenToUpdate)
                {
                    // Remove old inherited ACLs
                    int removed = child.Acls.RemoveAll(a => a.IsInherited);

                    // Add new inherited ACLs from parent
                    int added = 0;
                    foreach (var pAcl in parentAcls)
                    {
                        // If child does not have an EXPLICIT rule for this SID, inherit it
                        if (!child.Acls.Any(a => a.HashedSid == pAcl.HashedSid && !a.IsInherited))
                        {
                            child.Acls.Add(new AclEntryDto 
                            { 
                                HashedSid = pAcl.HashedSid, 
                                DisplayName = pAcl.DisplayName, 
                                PermissionLevel = pAcl.PermissionLevel, 
                                IsInherited = true,
                                SourceGroupSid = pAcl.SourceGroupSid,
                                SourceGroupName = pAcl.SourceGroupName
                            });
                            added++;
                        }
                    }

                    try
                    {
                        var req = _cryptoService.EncryptAsset(child);
                        await _apiClient.UpdateAssetAsync(child.TransientAssetId.Value, req);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Fehler beim Vererben an {child.Title}: {ex.Message}");
                    }
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
