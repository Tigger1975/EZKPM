using Avalonia.Controls;
using Avalonia.Interactivity;
using EZKPM.Client.Core.Services;
using EZKPM.Shared.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Avalonia.Input.Platform;

namespace EZKPM.Client.Desktop.Views;

public partial class AdminDashboardWindow : Window
{
    private readonly VaultApiClient _apiClient;
    private List<EZKPM.Shared.Contracts.VaultAssetPayload> _decryptedAssets;
    private readonly Func<Guid, EZKPM.Shared.Contracts.VaultAssetPayload> _getAssetOnDemand;

    public AdminDashboardWindow()
    {
        InitializeComponent();
    }

    public AdminDashboardWindow(VaultApiClient apiClient, List<EZKPM.Shared.Contracts.VaultAssetPayload> decryptedAssets, Func<Guid, EZKPM.Shared.Contracts.VaultAssetPayload> getAssetOnDemand) : this()
    {
        _apiClient = apiClient;
        _decryptedAssets = decryptedAssets;
        _getAssetOnDemand = getAssetOnDemand;
        LoadAdminStatus();
        _ = LoadAlertsAsync();
        var config = EZKPM.Client.Desktop.Services.ConfigurationManager.CurrentConfig;

        // Note: Controls inside tabs are lazily evaluated in Avalonia.
        // We populate them in TabControl_SelectionChanged instead.
        
        // The first tab is already selected, so we load its data now:
        _ = LoadAdminsAsync();
    }

    private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_apiClient == null) return; // Prevent crash during InitializeComponent
        
        if (e.Source is TabControl tc)
        {
            if (tc.SelectedIndex == 0)
            {
                _ = LoadAdminsAsync();
            }
            else if (tc.SelectedIndex == 5)
            {
                _ = LoadMachinesForLogsAsync();
            }
            
            var config = EZKPM.Client.Desktop.Services.ConfigurationManager.CurrentConfig;

            var serverBox = this.FindControl<TextBox>("SmtpServerTextBox");
            if (serverBox != null && string.IsNullOrEmpty(serverBox.Text)) serverBox.Text = config.SmtpServer;

            var portBox = this.FindControl<TextBox>("SmtpPortTextBox");
            if (portBox != null && string.IsNullOrEmpty(portBox.Text)) portBox.Text = config.SmtpPort.ToString();

            var senderBox = this.FindControl<TextBox>("SmtpSenderTextBox");
            if (senderBox != null && string.IsNullOrEmpty(senderBox.Text)) senderBox.Text = config.SmtpSender;

            var subjectBox = this.FindControl<TextBox>("EmailSubjectTextBox");
            if (subjectBox != null && string.IsNullOrEmpty(subjectBox.Text)) subjectBox.Text = config.SmtpSubjectTemplate;

            var bodyBox = this.FindControl<TextBox>("EmailBodyTextBox");
            if (bodyBox != null && string.IsNullOrEmpty(bodyBox.Text)) bodyBox.Text = config.SmtpBodyTemplate;
        }
    }

    private async void LoadAdminStatus()
    {
        try
        {
            var status = await _apiClient.GetAdminStatusAsync();
            if (status != null && status.IsBootstrapActive)
            {
                BootstrapWarningText.IsVisible = true;
            }
        }
        catch { }
    }

    private async Task LoadMachinesForLogsAsync()
    {
        try
        {
            var machines = await _apiClient.HttpClient.GetFromJsonAsync<System.Collections.Generic.List<string>>("/api/v1/log/machines");
            if (machines != null)
            {
                var autoComplete = this.FindControl<AutoCompleteBox>("DeviceLogMachineNameAutoCompleteBox");
                if (autoComplete != null)
                {
                    autoComplete.ItemsSource = machines;
                }
            }
        }
        catch { }
    }

    private string GetEnvironmentLogKeyPassword()
    {
        if (_decryptedAssets == null || _getAssetOnDemand == null) return null;
        var logKeyAsset = _decryptedAssets.FirstOrDefault(a => a.Title == "EnvironmentLogKey");
        if (logKeyAsset == null || !logKeyAsset.TransientAssetId.HasValue) return null;
        
        // Use JIT Decryption since ScrubSensitiveData wiped it from RAM
        var rawAsset = _getAssetOnDemand(logKeyAsset.TransientAssetId.Value);
        return rawAsset?.Password;
    }

    private async void CopyLogKeyButton_Click(object sender, RoutedEventArgs e)
    {
        string privateKeyBase64 = GetEnvironmentLogKeyPassword();
        if (!string.IsNullOrEmpty(privateKeyBase64))
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(privateKeyBase64);
                var dialog = new ConfirmationDialog("Environment Log Key wurde in die Zwischenablage kopiert.");
                await dialog.ShowDialog(this);
            }
        }
        else
        {
            var dialog = new ConfirmationDialog("Log Key nicht verfügbar (fehlende Berechtigung oder noch nicht generiert).");
            await dialog.ShowDialog(this);
        }
    }

    private void LoadEnvironmentLogKey()
    {
        // No longer updates UI. Log Key is accessed directly via copy button or log extraction.
    }

    private async void ExtractLogsButton_Click(object sender, RoutedEventArgs e)
    {
        var autoComplete = this.FindControl<AutoCompleteBox>("DeviceLogMachineNameAutoCompleteBox");
        string machineName = autoComplete?.Text?.Trim();
        
        var outputBox = this.FindControl<TextBox>("ExtractedLogsTextBox");
        if (outputBox == null) return;

        if (string.IsNullOrEmpty(machineName))
        {
            outputBox.Text = "Bitte geben Sie einen Machine Name ein.";
            return;
        }

        string privateKeyBase64 = GetEnvironmentLogKeyPassword();

        if (string.IsNullOrEmpty(privateKeyBase64))
        {
            outputBox.Text = "EnvironmentLogKey nicht verfügbar. Entschlüsselung nicht möglich.";
            return;
        }

        outputBox.Text = $"Lade Logs für {machineName}...\n";
        
        try
        {
            var response = await _apiClient.HttpClient.GetAsync($"/api/v1/log/{Uri.EscapeDataString(machineName)}");
            if (response.IsSuccessStatusCode)
            {
                var logs = await response.Content.ReadFromJsonAsync<List<EZKPM.Client.Desktop.Services.ClientLogDto>>();
                if (logs == null || logs.Count == 0)
                {
                    outputBox.Text += "Keine Logs gefunden.\n";
                    return;
                }

                using var rsa = System.Security.Cryptography.RSA.Create();
                rsa.ImportPkcs8PrivateKey(Convert.FromBase64String(privateKeyBase64), out _);

                var sb = new System.Text.StringBuilder();
                foreach (var log in logs)
                {
                    sb.Append($"[{log.Timestamp:yyyy-MM-dd HH:mm:ss}] [{log.Level}] {log.Username}: ");
                    
                    if (log.Message != null && log.Message.StartsWith("ENV_ENC:"))
                    {
                        try
                        {
                            var parts = log.Message.Substring(8).Split(':');
                            if (parts.Length == 4)
                            {
                                byte[] encryptedAesKey = Convert.FromBase64String(parts[0]);
                                byte[] nonce = Convert.FromBase64String(parts[1]);
                                byte[] cipherText = Convert.FromBase64String(parts[2]);
                                byte[] tag = Convert.FromBase64String(parts[3]);

                                byte[] aesKey = rsa.Decrypt(encryptedAesKey, System.Security.Cryptography.RSAEncryptionPadding.OaepSHA256);
                                byte[] plainText = new byte[cipherText.Length];

                                using (var aesGcm = new System.Security.Cryptography.AesGcm(aesKey, 16))
                                {
                                    aesGcm.Decrypt(nonce, cipherText, tag, plainText);
                                }
                                
                                sb.AppendLine(System.Text.Encoding.UTF8.GetString(plainText));
                            }
                            else
                            {
                                sb.AppendLine("[Entschlüsselungsfehler: Ungültiges Format]");
                            }
                        }
                        catch (Exception ex)
                        {
                            sb.AppendLine($"[Entschlüsselungsfehler: {ex.Message}]");
                        }
                    }
                    else
                    {
                        sb.AppendLine(log.Message ?? "");
                    }
                }
                outputBox.Text = sb.ToString();
            }
            else
            {
                outputBox.Text += $"Server meldet Fehler: {response.StatusCode}\n";
            }
        }
        catch (Exception ex)
        {
            outputBox.Text += $"Ein Fehler ist aufgetreten: {ex.Message}";
        }
    }

    private async void SearchAdminButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new AdPickerWindow(EZKPM.Client.Desktop.Services.AdPickerFilterMode.ActiveOnly);
        await picker.ShowDialog(this);

        if (picker.SelectedPrincipal != null)
        {
            _selectedUserForAdmin = picker.SelectedPrincipal;
            SelectedAdminText.Text = $"{_selectedUserForAdmin.DisplayName} ({_selectedUserForAdmin.SamAccountName})";
        }
    }

    private EZKPM.Client.Desktop.Services.AdPrincipal _selectedUserForInvite;

    private async void SearchInviteButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new AdPickerWindow(EZKPM.Client.Desktop.Services.AdPickerFilterMode.ActiveOnly);
        await picker.ShowDialog(this);

        if (picker.SelectedPrincipal != null)
        {
            _selectedUserForInvite = picker.SelectedPrincipal;
            var textBlock = this.FindControl<TextBlock>("SelectedInviteText");
            if (textBlock != null) textBlock.Text = $"{_selectedUserForInvite.DisplayName} ({_selectedUserForInvite.SamAccountName})";
            
            var btn = this.FindControl<Button>("SendInviteButton");
            if (btn != null) btn.IsEnabled = true;
        }
    }

    private async void SendInviteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedUserForInvite == null) return;

        try
        {
            // 1. Hash SID and Username locally (Zero-Knowledge)
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var sidHash = Convert.ToBase64String(sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(_selectedUserForInvite.Sid)));
            var nameHash = Convert.ToBase64String(sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(_selectedUserForInvite.SamAccountName)));

            // 2. Request Pairing Code from Server
            var payload = new { HashedSid = sidHash, HashedUsername = nameHash };
            var response = await _apiClient.HttpClient.PostAsJsonAsync("/api/v1/auth/invite", payload);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
                var pairingCode = result.GetProperty("pairingCode").GetString();

                // 3. Open local E-Mail client (mailto)
                string serverUrl = EZKPM.Client.Desktop.Services.ConfigurationManager.CurrentConfig.ServerUrl;
                string email = !string.IsNullOrWhiteSpace(_selectedUserForInvite.EmailAddress) 
                    ? _selectedUserForInvite.EmailAddress 
                    : $"{_selectedUserForInvite.SamAccountName}@{System.DirectoryServices.ActiveDirectory.Domain.GetCurrentDomain().Name}";
                    
                string subject = Uri.EscapeDataString("Ihre Einladung für EZKPM / Your invitation for EZKPM (Ironclad Vault)");
                
                string bodyText = 
                    $"Hallo {_selectedUserForInvite.DisplayName},\n\n" +
                    $"Sie wurden zur Nutzung von EZKPM (Ironclad Vault) eingeladen.\n" +
                    $"1. Laden Sie den Client hier herunter: {serverUrl}\n" +
                    $"2. Klicken Sie nach der Installation auf folgenden Link, um sich zu verbinden:\n" +
                    $"   ezkpm://pair?code={pairingCode}\n\n" +
                    $"Alternativ können Sie den Code manuell eingeben: {pairingCode}\n\n" +
                    $"---\n\n" +
                    $"Hello {_selectedUserForInvite.DisplayName},\n\n" +
                    $"You have been invited to use EZKPM (Ironclad Vault).\n" +
                    $"1. Download the client here: {serverUrl}\n" +
                    $"2. After installation, click the following link to connect:\n" +
                    $"   ezkpm://pair?code={pairingCode}\n\n" +
                    $"Alternatively, you can enter the code manually: {pairingCode}";

                string body = Uri.EscapeDataString(bodyText);
                
                string mailtoUri = $"mailto:{email}?subject={subject}&body={body}";

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = mailtoUri,
                    UseShellExecute = true
                });

                var dialog = new ConfirmationDialog("Einladung wurde generiert und E-Mail-Programm geöffnet.");
                await dialog.ShowDialogAsync(this);

                _selectedUserForInvite = null;
                var textBlock = this.FindControl<TextBlock>("SelectedInviteText");
                if (textBlock != null) textBlock.Text = "Keine Auswahl";
                var btn = this.FindControl<Button>("SendInviteButton");
                if (btn != null) btn.IsEnabled = false;
            }
            else
            {
                string err = await response.Content.ReadAsStringAsync();
                var dialog = new ConfirmationDialog($"Fehler: {err}");
                await dialog.ShowDialogAsync(this);
            }
        }
        catch (Exception ex)
        {
            var dialog = new ConfirmationDialog($"Fehler beim Senden: {ex.Message}");
            await dialog.ShowDialogAsync(this);
        }
    }

    private EZKPM.Client.Desktop.Services.AdPrincipal _bulkInviteTarget;

    private async void BulkInviteTargetButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new AdPickerWindow(EZKPM.Client.Desktop.Services.AdPickerFilterMode.ActiveOnly);
        await picker.ShowDialog(this);

        if (picker.SelectedPrincipal != null)
        {
            _bulkInviteTarget = picker.SelectedPrincipal;
            var textBlock = this.FindControl<TextBlock>("BulkInviteTargetText");
            if (textBlock != null) textBlock.Text = $"{_bulkInviteTarget.DisplayName} ({_bulkInviteTarget.Type})";
        }
    }

    private void InsertPlaceholder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Content is string placeholder)
        {
            var bodyBox = this.FindControl<TextBox>("EmailBodyTextBox");
            if (bodyBox != null)
            {
                int caretIndex = bodyBox.CaretIndex;
                if (caretIndex < 0) caretIndex = bodyBox.Text?.Length ?? 0;
                
                string currentText = bodyBox.Text ?? "";
                bodyBox.Text = currentText.Insert(caretIndex, placeholder);
                
                // Set cursor after the inserted text
                bodyBox.CaretIndex = caretIndex + placeholder.Length;
                bodyBox.Focus();
            }
        }
    }

    private async void BulkInviteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_bulkInviteTarget == null)
        {
            var dialog = new ConfirmationDialog("Bitte wählen Sie zuerst eine Ziel-Gruppe oder einen Benutzer aus.");
            await dialog.ShowDialogAsync(this);
            return;
        }

        string smtpServer = this.FindControl<TextBox>("SmtpServerTextBox")?.Text;
        string smtpPortStr = this.FindControl<TextBox>("SmtpPortTextBox")?.Text;
        string smtpSender = this.FindControl<TextBox>("SmtpSenderTextBox")?.Text;
        string emailSubjectTemplate = this.FindControl<TextBox>("EmailSubjectTextBox")?.Text ?? "Einladung";
        string emailBodyTemplate = this.FindControl<TextBox>("EmailBodyTextBox")?.Text ?? "Code: {PairingCode}";

        if (string.IsNullOrWhiteSpace(smtpServer) || string.IsNullOrWhiteSpace(smtpSender))
        {
            var dialog = new ConfirmationDialog("Bitte füllen Sie SMTP Server und Absender E-Mail aus.");
            await dialog.ShowDialogAsync(this);
            return;
        }

        if (!int.TryParse(smtpPortStr, out int smtpPort)) smtpPort = 25;

        // Save config
        var config = EZKPM.Client.Desktop.Services.ConfigurationManager.CurrentConfig;
        config.SmtpServer = smtpServer;
        config.SmtpPort = smtpPort;
        config.SmtpSender = smtpSender;
        config.SmtpSubjectTemplate = emailSubjectTemplate;
        config.SmtpBodyTemplate = emailBodyTemplate;
        EZKPM.Client.Desktop.Services.ConfigurationManager.SaveConfig();

        // Fetch target users
        var allUsers = await Task.Run(() => 
        {
            var list = new System.Collections.Generic.List<EZKPM.Client.Desktop.Services.AdPrincipal>();
            try
            {
                using var ctx = new System.DirectoryServices.AccountManagement.PrincipalContext(System.DirectoryServices.AccountManagement.ContextType.Domain);
                
                if (_bulkInviteTarget.Type == "Group")
                {
                    using var group = System.DirectoryServices.AccountManagement.GroupPrincipal.FindByIdentity(ctx, _bulkInviteTarget.Sid);
                    if (group != null)
                    {
                        foreach (var result in group.GetMembers(true))
                        {
                            if (result is System.DirectoryServices.AccountManagement.UserPrincipal u && u.Enabled == true && u.Sid != null && !string.IsNullOrEmpty(u.EmailAddress))
                            {
                                list.Add(new EZKPM.Client.Desktop.Services.AdPrincipal
                                {
                                    Sid = u.Sid.Value,
                                    DisplayName = u.DisplayName ?? u.SamAccountName,
                                    SamAccountName = u.SamAccountName,
                                    EmailAddress = u.EmailAddress,
                                    IsAccountDisabled = false
                                });
                            }
                        }
                    }
                }
                else
                {
                    using var u = System.DirectoryServices.AccountManagement.UserPrincipal.FindByIdentity(ctx, _bulkInviteTarget.Sid);
                    if (u != null && u.Enabled == true && u.Sid != null && !string.IsNullOrEmpty(u.EmailAddress))
                    {
                        list.Add(new EZKPM.Client.Desktop.Services.AdPrincipal
                        {
                            Sid = u.Sid.Value,
                            DisplayName = u.DisplayName ?? u.SamAccountName,
                            SamAccountName = u.SamAccountName,
                            EmailAddress = u.EmailAddress,
                            IsAccountDisabled = false
                        });
                    }
                }
            }
            catch { }
            return list;
        });

        if (allUsers.Count == 0)
        {
            var dialog = new ConfirmationDialog("Keine aktiven AD-Benutzer mit E-Mail-Adresse in der Auswahl gefunden.");
            await dialog.ShowDialogAsync(this);
            return;
        }

        var confirmDialog = new ConfirmationDialog($"Möchten Sie wirklich an {allUsers.Count} Benutzer eine Einladung senden?\nDer Vorgang läuft im Hintergrund.");
        if (!await confirmDialog.ShowDialogAsync(this)) return;

        var btn = this.FindControl<Button>("BulkInviteButton");
        if (btn != null) btn.IsEnabled = false;

        string serverUrl = EZKPM.Client.Desktop.Services.ConfigurationManager.CurrentConfig.ServerUrl;
        int successCount = 0;
        int errorCount = 0;
        int skippedCount = 0;
        var errorMessages = new System.Collections.Concurrent.ConcurrentDictionary<string, int>();

        await Task.Run(async () =>
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            using var smtpClient = new System.Net.Mail.SmtpClient(smtpServer, smtpPort);
            
            // SMTP is often slow, we do it synchronously per user to not overwhelm the API or Exchange Server
            foreach (var user in allUsers)
            {
                try
                {
                    var sidHash = Convert.ToBase64String(sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(user.Sid)));
                    var nameHash = Convert.ToBase64String(sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(user.SamAccountName)));

                    var payload = new { HashedSid = sidHash, HashedUsername = nameHash };
                    var response = await _apiClient.HttpClient.PostAsJsonAsync("/api/v1/auth/invite", payload);

                    if (response.IsSuccessStatusCode)
                    {
                        var result = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
                        var pairingCode = result.GetProperty("pairingCode").GetString();

                        string targetEmail = user.EmailAddress;
                        
                        string finalSubject = emailSubjectTemplate
                            .Replace("{DisplayName}", user.DisplayName)
                            .Replace("{SamAccountName}", user.SamAccountName)
                            .Replace("{ServerUrl}", serverUrl)
                            .Replace("{PairingCode}", pairingCode);

                        string finalBody = emailBodyTemplate
                            .Replace("{DisplayName}", user.DisplayName)
                            .Replace("{SamAccountName}", user.SamAccountName)
                            .Replace("{ServerUrl}", serverUrl)
                            .Replace("{PairingCode}", pairingCode);

                        var mailMessage = new System.Net.Mail.MailMessage(smtpSender, targetEmail, finalSubject, finalBody);
                        await smtpClient.SendMailAsync(mailMessage);
                        successCount++;
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
                    {
                        skippedCount++;
                    }
                    else
                    {
                        errorCount++;
                        string errorStr = $"HTTP {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}";
                        errorMessages.AddOrUpdate(errorStr, 1, (k, v) => v + 1);
                    }
                }
                catch (Exception ex)
                {
                    errorCount++;
                    errorMessages.AddOrUpdate(ex.Message, 1, (k, v) => v + 1);
                }
            }
        });

        Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
        {
            if (btn != null) btn.IsEnabled = true;
            
            string finalMessage = $"Bulk Invite abgeschlossen.\nErfolgreich gesendet: {successCount}\nÜbersprungen (bereits aktiv): {skippedCount}\nFehler: {errorCount}";
            if (errorCount > 0)
            {
                finalMessage += "\n\nFehlerdetails:\n";
                foreach (var kvp in errorMessages.Take(5))
                {
                    finalMessage += $"- {kvp.Value}x: {kvp.Key}\n";
                }
                if (errorMessages.Count > 5)
                {
                    finalMessage += $"- ... und {errorMessages.Count - 5} weitere Fehlerarten.";
                }
            }

            var dialog = new ConfirmationDialog(finalMessage);
            await dialog.ShowDialogAsync(this);
        });
    }

    private async void SearchDisabledUserButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new AdPickerWindow(EZKPM.Client.Desktop.Services.AdPickerFilterMode.DisabledOnly);
        await picker.ShowDialog(this);

        if (picker.SelectedPrincipal != null)
        {
            TargetUserSidTextBox.Text = picker.SelectedPrincipal.Sid;
        }
    }

    private async void PickSourcePersonButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new AdPickerWindow(EZKPM.Client.Desktop.Services.AdPickerFilterMode.ActiveOnly);
        await picker.ShowDialog(this);
        if (picker.SelectedPrincipal != null)
        {
            _sourcePerson = picker.SelectedPrincipal;
            SourcePersonText.Text = $"{_sourcePerson.DisplayName} ({_sourcePerson.Sid})";
        }
    }

    private async void PickTargetPersonButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new AdPickerWindow(EZKPM.Client.Desktop.Services.AdPickerFilterMode.ActiveOnly);
        await picker.ShowDialog(this);
        if (picker.SelectedPrincipal != null)
        {
            _targetPerson = picker.SelectedPrincipal;
            TargetPersonText.Text = $"{_targetPerson.DisplayName} ({_targetPerson.Sid})";
        }
    }

    private EZKPM.Client.Desktop.Services.AdPrincipal _selectedUserForAdmin;


    private async void MakeAdminButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedUserForAdmin == null) return;

        try
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var targetHash = Convert.ToBase64String(sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(_selectedUserForAdmin.Sid)));
            var req = new SetAdminRequestDto { TargetHashedSid = targetHash, IsAdmin = true };
            var response = await _apiClient.HttpClient.PostAsJsonAsync("/api/v1/recovery/set-admin", req);
            if (response.IsSuccessStatusCode)
            {
                var dialog = new ConfirmationDialog($"{_selectedUserForAdmin.DisplayName} ist nun Admin.");
                await dialog.ShowDialogAsync(this);
                LoadAdminStatus(); // Refresh status (might disable bootstrap mode)
                _selectedUserForAdmin = null;
                SelectedAdminText.Text = "Keine Auswahl";
                _ = LoadAdminsAsync();
            }
            else
            {
                string err = await response.Content.ReadAsStringAsync();
                var dialog = new ConfirmationDialog($"Fehler: {err}");
                await dialog.ShowDialogAsync(this);
            }
        }
        catch (Exception ex)
        {
            var dialog = new ConfirmationDialog($"Fehler: {ex.Message}");
            await dialog.ShowDialogAsync(this);
        }
    }

    private async void RemoveAdminButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedUserForAdmin == null) return;

        try
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var targetHash = Convert.ToBase64String(sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(_selectedUserForAdmin.Sid)));
            var req = new SetAdminRequestDto { TargetHashedSid = targetHash, IsAdmin = false };
            var response = await _apiClient.HttpClient.PostAsJsonAsync("/api/v1/recovery/set-admin", req);
            if (response.IsSuccessStatusCode)
            {
                var dialog = new ConfirmationDialog($"{_selectedUserForAdmin.DisplayName} ist nun KEIN Admin mehr.");
                await dialog.ShowDialogAsync(this);
                _selectedUserForAdmin = null;
                SelectedAdminText.Text = "Keine Auswahl";
                _ = LoadAdminsAsync();
            }
            else
            {
                string err = await response.Content.ReadAsStringAsync();
                var dialog = new ConfirmationDialog($"Fehler: {err}");
                await dialog.ShowDialogAsync(this);
            }
        }
        catch (Exception ex)
        {
            var dialog = new ConfirmationDialog($"Fehler: {ex.Message}");
            await dialog.ShowDialogAsync(this);
        }
    }

    private async void RevokeAdminFromList_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string sid)
        {
            try
            {
                using var sha256 = System.Security.Cryptography.SHA256.Create();
                var targetHash = Convert.ToBase64String(sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(sid)));
                var req = new SetAdminRequestDto { TargetHashedSid = targetHash, IsAdmin = false };
                var response = await _apiClient.HttpClient.PostAsJsonAsync("/api/v1/recovery/set-admin", req);
                if (response.IsSuccessStatusCode)
                {
                    var dialog = new ConfirmationDialog($"Admin-Rechte wurden erfolgreich entzogen.");
                    await dialog.ShowDialogAsync(this);
                    _ = LoadAdminsAsync();
                }
                else
                {
                    string err = await response.Content.ReadAsStringAsync();
                    var dialog = new ConfirmationDialog($"Fehler: {err}");
                    await dialog.ShowDialogAsync(this);
                }
            }
            catch (Exception ex)
            {
                var dialog = new ConfirmationDialog($"Fehler: {ex.Message}");
                await dialog.ShowDialogAsync(this);
            }
        }
    }

    private async Task LoadAdminsAsync()
    {
        try
        {
            var adminHashes = await _apiClient.HttpClient.GetFromJsonAsync<System.Collections.Generic.List<string>>("/api/v1/recovery/admins");
            if (adminHashes == null || adminHashes.Count == 0)
            {
                CurrentAdminsList.ItemsSource = null;
                return;
            }

            var resolvedAdmins = await Task.Run(() => 
            {
                var list = new System.Collections.Generic.List<AdminUserViewModel>();
                try
                {
                    using var sha256 = System.Security.Cryptography.SHA256.Create();
                    
                    var entry = new System.DirectoryServices.DirectoryEntry();
                    using var searcher = new System.DirectoryServices.DirectorySearcher(entry)
                    {
                        Filter = "(&(objectCategory=person)(objectClass=user))",
                        PageSize = 1000
                    };
                    searcher.PropertiesToLoad.Add("objectSid");
                    searcher.PropertiesToLoad.Add("displayName");
                    searcher.PropertiesToLoad.Add("sAMAccountName");
                    searcher.PropertiesToLoad.Add("userAccountControl");

                    using var results = searcher.FindAll();
                    foreach (System.DirectoryServices.SearchResult result in results)
                    {
                        if (result.Properties["objectSid"].Count > 0)
                        {
                            var sidBytes = (byte[])result.Properties["objectSid"][0];
                            var sid = new System.Security.Principal.SecurityIdentifier(sidBytes, 0).ToString();
                            var hash = Convert.ToBase64String(sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(sid)));
                            
                            if (adminHashes.Contains(hash))
                            {
                                int uac = result.Properties["userAccountControl"].Count > 0 ? (int)result.Properties["userAccountControl"][0] : 0;
                                bool disabled = (uac & 2) == 2; // ADS_UF_ACCOUNTDISABLE
                                
                                list.Add(new AdminUserViewModel
                                {
                                    Sid = sid,
                                    DisplayName = result.Properties["displayName"].Count > 0 ? result.Properties["displayName"][0].ToString() : sid,
                                    SamAccountName = result.Properties["sAMAccountName"].Count > 0 ? result.Properties["sAMAccountName"][0].ToString() : "",
                                    IsAccountDisabled = disabled
                                });
                            }
                        }
                    }
                }
                catch { }
                return list;
            });

            CurrentAdminsList.ItemsSource = resolvedAdmins;
        }
        catch (Exception ex)
        {
            var dialog = new ConfirmationDialog($"Fehler beim Laden der Admins: {ex.Message}");
            await dialog.ShowDialogAsync(this);
        }
    }

    private void ResolveAdminsButton_Click(object sender, RoutedEventArgs e)
    {
        _ = LoadAdminsAsync();
    }

    private async void RefreshRecoveryButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var pending = await _apiClient.HttpClient.GetFromJsonAsync<System.Collections.Generic.List<dynamic>>("/api/v1/recovery/pending");
            PendingRecoveryList.ItemsSource = pending;
        }
        catch { }
    }

    private async void ApproveRecoveryButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = PendingRecoveryList.SelectedItem as dynamic;
        if (selected == null) return;

        try
        {
            // Placeholder: Admin generates a share blob using their master key
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var currentUser = EZKPM.Client.Desktop.Services.AdSearchService.GetCurrentUser();
            var adminHashedSid = currentUser != null ? Convert.ToBase64String(sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(currentUser.Sid))) : "";

            var req = new ProvideRecoveryShareDto 
            { 
                RecoveryRequestId = Guid.Parse(selected.GetProperty("id").GetString()), 
                AdminHashedSid = adminHashedSid,
                EncryptedShareBlob = "SIMULATED_SHARE_BLOB"
            };
            await _apiClient.ApproveRecoveryAsync(req);
            
            var dialog = new ConfirmationDialog($"Freigabe gesendet.");
            await dialog.ShowDialog(this);
            RefreshRecoveryButton_Click(null, null);
        }
        catch (Exception ex)
        {
            var dialog = new ConfirmationDialog($"Fehler bei Freigabe: {ex.Message}");
            await dialog.ShowDialog(this);
        }
    }

    private async void InitiateRecoveryButton_Click(object sender, RoutedEventArgs e)
    {
        string targetSid = TargetUserSidTextBox.Text;
        if (string.IsNullOrWhiteSpace(targetSid)) return;

        try
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var hashedTarget = Convert.ToBase64String(sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(targetSid)));
            var req = new InitiateRecoveryRequestDto 
            { 
                HashedSid = hashedTarget, 
                EphemeralUserPubKey = "ADMIN_EPHEMERAL_PUBKEY"
            };
            await _apiClient.RequestRecoveryAsync(req);
            
            var dialog = new ConfirmationDialog($"Recovery-Anfrage erfolgreich erstellt. Warte auf 2 Admins.");
            await dialog.ShowDialog(this);
            TargetUserSidTextBox.Text = "";
        }
        catch (Exception ex)
        {
            var dialog = new ConfirmationDialog($"Fehler: {ex.Message}");
            await dialog.ShowDialog(this);
        }
    }

    private EZKPM.Client.Desktop.Services.AdPrincipal _sourcePerson;
    private EZKPM.Client.Desktop.Services.AdPrincipal _targetPerson;

    private async void LinkPersonsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_sourcePerson == null || _targetPerson == null)
        {
            var err = new ConfirmationDialog("Bitte wählen Sie Quell- und Ziel-Account aus.");
            await err.ShowDialog(this);
            return;
        }

        try
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var req = new LinkPersonDto 
            { 
                SourceHashedSid = Convert.ToBase64String(sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(_sourcePerson.Sid))),
                TargetHashedSid = Convert.ToBase64String(sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(_targetPerson.Sid)))
            };
            
            var response = await _apiClient.HttpClient.PostAsJsonAsync("/api/v1/recovery/link-person", req);
            
            if (response.IsSuccessStatusCode)
            {
                var dialog = new ConfirmationDialog($"Erfolg! {_sourcePerson.DisplayName} ist nun physisch mit {_targetPerson.DisplayName} verknüpft.");
                await dialog.ShowDialog(this);
                
                _sourcePerson = null;
                _targetPerson = null;
                SourcePersonText.Text = "Keine Auswahl";
                TargetPersonText.Text = "Keine Auswahl";
            }
            else
            {
                string errMsg = await response.Content.ReadAsStringAsync();
                var dialog = new ConfirmationDialog($"Fehler: {errMsg}");
                await dialog.ShowDialog(this);
            }
        }
        catch (Exception ex)
        {
            var dialog = new ConfirmationDialog($"Fehler: {ex.Message}");
            await dialog.ShowDialog(this);
        }
    }
    private async void RefreshAlertsButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadAlertsAsync();
    }

    private async void ForceScanButton_Click(object sender, RoutedEventArgs e)
    {
        var scanner = new EZKPM.Client.Desktop.Services.VulnerabilityScannerService(_apiClient);
        await scanner.CheckForVulnerabilitiesAsync(async (report) => 
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
            {
                var dialog = new ConfirmationDialog(report);
                await dialog.ShowDialogAsync(this);
            });
        }, force: true);
        
        await LoadAlertsAsync();
    }

    private async Task LoadAlertsAsync()
    {
        try
        {
            var response = await _apiClient.HttpClient.GetAsync("/api/v1/security-alerts");
            if (response.IsSuccessStatusCode)
            {
                var alerts = await response.Content.ReadFromJsonAsync<System.Collections.Generic.List<EZKPM.Shared.Contracts.SecurityAlertResponseDto>>();
                SecurityAlertsList.ItemsSource = alerts?.Where(a => !a.IsResolved).ToList();
            }
        }
        catch (Exception ex)
        {
            Program.LogDebug($"Fehler beim Laden der Security Alerts: {ex.Message}");
        }
    }

    private async void ResolveAlertButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Guid alertId)
        {
            try
            {
                var response = await _apiClient.HttpClient.PostAsync($"/api/v1/security-alerts/{alertId}/resolve", null);
                if (response.IsSuccessStatusCode)
                {
                    await LoadAlertsAsync();
                }
                else
                {
                    var err = await response.Content.ReadAsStringAsync();
                    var dialog = new ConfirmationDialog($"Fehler: {err}");
                    await dialog.ShowDialogAsync(this);
                }
            }
            catch (Exception ex)
            {
                var dialog = new ConfirmationDialog($"Fehler: {ex.Message}");
                await dialog.ShowDialogAsync(this);
            }
        }
    }
}

public class AdminUserViewModel
{
    public string Sid { get; set; }
    public string DisplayName { get; set; }
    public string SamAccountName { get; set; }
    public bool IsAccountDisabled { get; set; }
    public string StatusText => IsAccountDisabled ? "Deaktiviert" : "Aktiv";
    public string StatusColor => IsAccountDisabled ? "#EF4444" : "#10B981";
}
