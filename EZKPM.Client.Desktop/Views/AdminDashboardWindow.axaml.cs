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
            var req = new SetAdminRequestDto { TargetAdSid = _selectedUserForAdmin.Sid, IsAdmin = true };
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
            var req = new SetAdminRequestDto { TargetAdSid = _selectedUserForAdmin.Sid, IsAdmin = false };
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
            var req = new ProvideRecoveryShareDto 
            { 
                RecoveryRequestId = Guid.Parse(selected.GetProperty("id").GetString()), 
                AdminSid = EZKPM.Client.Desktop.Services.AdSearchService.GetCurrentUser()?.Sid,
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
            var req = new InitiateRecoveryRequestDto 
            { 
                AdSid = targetSid, 
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
            var req = new LinkPersonDto 
            { 
                SourceAdSid = _sourcePerson.Sid,
                TargetAdSid = _targetPerson.Sid
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
