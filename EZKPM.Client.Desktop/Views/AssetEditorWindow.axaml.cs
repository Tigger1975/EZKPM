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
    private Avalonia.Threading.DispatcherTimer? _totpTimer;

    public event EventHandler? AssetSaved;

    public AssetEditorWindow() : this(null, null) { }
    public AssetEditorWindow(VaultAssetPayload? payload = null, List<VaultAssetPayload>? allAssets = null)
    {
        InitializeComponent();
        
        _totpTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _totpTimer.Tick += TotpTimer_Tick;
        _totpTimer.Start();

        var httpClient = new HttpClient { BaseAddress = new Uri("http://localhost:5117") };
        _apiClient = new VaultApiClient(httpClient);
        _cryptoService = new VaultCryptoService(new HybridPqcKeyWrapper());

        AttachmentsListBox.ItemsSource = _attachments;
        CustomFieldsListBox.ItemsSource = _customFields;
        AclsListBox.ItemsSource = _acls;
        
        // Populate ParentFolderComboBox if we have assets
        if (allAssets != null)
        {
            ParentFolderComboBox.ItemsSource = BuildHierarchicalFolderList(allAssets);
        }

        ResetEditor();

        if (payload != null)
        {
            _currentEditingAssetId = payload.TransientAssetId;

            // Set Title, Username, Password, etc.
            TitleTextBox.Text = payload.Title ?? "";
            UsernameTextBox.Text = payload.Username ?? "";
            PasswordTextBox.Text = payload.Password ?? "";
            UrlTextBox.Text = payload.Url ?? "";
            NotesTextBox.Text = payload.Notes ?? "";
            DetailedDescriptionTextBox.Text = payload.DetailedDescription ?? "";
            
            var totpSecretBox = this.FindControl<TextBox>("TotpSecretTextBox");
            if (totpSecretBox != null) totpSecretBox.Text = payload.TotpSecret ?? "";

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
        }
        
        // Ensure UI matches the selected type initially
        AssetTypeComboBox_SelectionChanged(null, null);
    }

    private void ResetEditor(bool keepType = false, bool keepTreeSelection = false, Guid? keepFolderId = null)
    {
        int previousTypeIndex = AssetTypeComboBox.SelectedIndex;
        
        _currentEditingAssetId = null;
        
        
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
        _customFields.Clear();
        _attachments.Clear();
        _acls.Clear();
        
        // Automatischer Owner = Ersteller
        var currentUser = Environment.UserDomainName + "\\" + Environment.UserName;
        if (currentUser.StartsWith("\\")) currentUser = Environment.UserName; // fallback
        _acls.Add(new AclEntryDto { AdSid = currentUser, PermissionLevel = 3 });

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
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
        {
            await clipboard.SetTextAsync(PasswordTextBox.Text ?? "");
            ShowStatus("Passwort in die Zwischenablage kopiert!");
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
            ShowStatus("Gesamte Asset-Details in die Zwischenablage kopiert!");
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

    private static string GetTotpCode(string base32Secret)
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
        try
        {
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
                Acls = _acls.ToList()
            };

            // 1. Encrypt Payload locally
            var requestDto = _cryptoService.EncryptAsset(payload);

            // 2. Send to Server (Update or Create)
            if (_currentEditingAssetId.HasValue)
            {
                await _apiClient.UpdateAssetAsync(_currentEditingAssetId.Value, requestDto);
                ShowStatus("Updated successfully!");
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
        if (isError) { }
        else if (isWarning) { }
        else { }

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

    private void AddAclButton_Click(object sender, RoutedEventArgs e)
    {
        _acls.Add(new AclEntryDto { AdSid = "", PermissionLevel = 1 });
    }

    private void RemoveAclButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is AclEntryDto acl)
        {
            _acls.Remove(acl);
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

    private async void OpenAdPickerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is AclEntryDto acl)
        {
            var picker = new AdPickerWindow();
            var result = await picker.ShowDialog<EZKPM.Client.Desktop.Services.AdPrincipal>(this);
            if (result != null)
            {
                var index = _acls.IndexOf(acl);
                if (index >= 0)
                {
                    _acls[index] = new AclEntryDto { AdSid = result.ToString(), PermissionLevel = acl.PermissionLevel };
                }
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
        }
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
