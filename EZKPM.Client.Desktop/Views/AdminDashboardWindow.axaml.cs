using Avalonia.Controls;
using Avalonia.Interactivity;
using EZKPM.Client.Core.Services;
using EZKPM.Shared.Contracts;
using System;
using System.Linq;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace EZKPM.Client.Desktop.Views;

public partial class AdminDashboardWindow : Window
{
    private readonly VaultApiClient _apiClient;

    public AdminDashboardWindow()
    {
        InitializeComponent();
    }

    public AdminDashboardWindow(VaultApiClient apiClient) : this()
    {
        _apiClient = apiClient;
        LoadAdminStatus();
        _ = LoadAlertsAsync();
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
                string email = $"{_selectedUserForInvite.SamAccountName}@{System.DirectoryServices.ActiveDirectory.Domain.GetCurrentDomain().Name}";
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

        if (string.IsNullOrWhiteSpace(smtpServer) || string.IsNullOrWhiteSpace(smtpSender))
        {
            var dialog = new ConfirmationDialog("Bitte füllen Sie SMTP Server und Absender E-Mail aus.");
            await dialog.ShowDialogAsync(this);
            return;
        }

        if (!int.TryParse(smtpPortStr, out int smtpPort)) smtpPort = 25;

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
            var dialog = new ConfirmationDialog("Keine aktiven AD-Benutzer mit E-Mail-Adresse gefunden.");
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

                        string targetEmail = $"{user.SamAccountName}@{System.DirectoryServices.ActiveDirectory.Domain.GetCurrentDomain().Name}";
                        string subject = "Ihre Einladung für EZKPM / Your invitation for EZKPM (Ironclad Vault)";
                        
                        string bodyText = 
                            $"Hallo {user.DisplayName},\n\n" +
                            $"Sie wurden zur Nutzung von EZKPM (Ironclad Vault) eingeladen.\n" +
                            $"1. Laden Sie den Client hier herunter: {serverUrl}\n" +
                            $"2. Klicken Sie nach der Installation auf folgenden Link, um sich zu verbinden:\n" +
                            $"   ezkpm://pair?code={pairingCode}\n\n" +
                            $"Alternativ können Sie den Code manuell eingeben: {pairingCode}\n\n" +
                            $"---\n\n" +
                            $"Hello {user.DisplayName},\n\n" +
                            $"You have been invited to use EZKPM (Ironclad Vault).\n" +
                            $"1. Download the client here: {serverUrl}\n" +
                            $"2. After installation, click the following link to connect:\n" +
                            $"   ezkpm://pair?code={pairingCode}\n\n" +
                            $"Alternatively, you can enter the code manually: {pairingCode}";

                        var mailMessage = new System.Net.Mail.MailMessage(smtpSender, targetEmail, subject, bodyText);
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
                    }
                }
                catch
                {
                    errorCount++;
                }
            }
        });

        Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
        {
            if (btn != null) btn.IsEnabled = true;
            var dialog = new ConfirmationDialog($"Bulk Invite abgeschlossen.\nErfolgreich gesendet: {successCount}\nÜbersprungen (bereits aktiv): {skippedCount}\nFehler: {errorCount}");
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

    private async void ResolveAdminsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // 1. Hole alle AD User SIDs
            var allUsers = await Task.Run(() => 
            {
                var list = new System.Collections.Generic.List<EZKPM.Client.Desktop.Services.AdPrincipal>();
                try
                {
                    var ctx = new System.DirectoryServices.AccountManagement.PrincipalContext(System.DirectoryServices.AccountManagement.ContextType.Domain);
                    var searcher = new System.DirectoryServices.AccountManagement.PrincipalSearcher(new System.DirectoryServices.AccountManagement.UserPrincipal(ctx));
                    foreach (var result in searcher.FindAll())
                    {
                        if (result.Sid != null)
                        {
                            list.Add(new EZKPM.Client.Desktop.Services.AdPrincipal
                            {
                                Sid = result.Sid.Value,
                                DisplayName = result.DisplayName ?? result.SamAccountName,
                                SamAccountName = result.SamAccountName,
                                IsAccountDisabled = result is System.DirectoryServices.AccountManagement.UserPrincipal u && (u.Enabled == false)
                            });
                        }
                    }
                }
                catch { }
                return list;
            });

            if (allUsers.Any())
            {
                // 2. Sende Liste an Server zum Filtern (Server hasht lokal und vergleicht)
                var sidsOnly = allUsers.Select(u => u.Sid).ToList();
                var response = await _apiClient.HttpClient.PostAsJsonAsync("/api/v1/recovery/filter-admins", sidsOnly);
                
                if (response.IsSuccessStatusCode)
                {
                    var adminSids = await response.Content.ReadFromJsonAsync<System.Collections.Generic.List<string>>();
                    if (adminSids != null)
                    {
                        // 3. Zeige die echten Admins an
                        var resolvedAdmins = allUsers.Where(u => adminSids.Contains(u.Sid)).ToList();
                        CurrentAdminsList.ItemsSource = resolvedAdmins;
                    }
                }
                else
                {
                    string err = await response.Content.ReadAsStringAsync();
                    var dialog = new ConfirmationDialog($"Fehler vom Server: {err}");
                    await dialog.ShowDialogAsync(this);
                }
            }
        }
        catch (Exception ex)
        {
            var dialog = new ConfirmationDialog($"Fehler: {ex.Message}");
            await dialog.ShowDialogAsync(this);
        }
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
