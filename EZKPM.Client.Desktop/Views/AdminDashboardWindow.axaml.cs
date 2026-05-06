using Avalonia.Controls;
using Avalonia.Interactivity;
using EZKPM.Client.Core.Services;
using EZKPM.Shared.Contracts;
using System;
using System.Linq;
using System.Net.Http.Json;

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
            AdminSearchResultsList.ItemsSource = new[] { picker.SelectedPrincipal };
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

    private async void MakeAdminButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = AdminSearchResultsList.SelectedItem as EZKPM.Client.Desktop.Services.AdPrincipal;
        if (selected == null) return;

        try
        {
            var req = new SetAdminRequestDto { TargetAdSid = selected.Sid, IsAdmin = true };
            var response = await _apiClient.HttpClient.PostAsJsonAsync("/api/v1/recovery/set-admin", req);
            if (response.IsSuccessStatusCode)
            {
                var dialog = new ConfirmationDialog($"{selected.DisplayName} ist nun Admin.");
                await dialog.ShowDialog(this);
                LoadAdminStatus(); // Refresh status (might disable bootstrap mode)
            }
            else
            {
                string err = await response.Content.ReadAsStringAsync();
                var dialog = new ConfirmationDialog($"Fehler: {err}");
                await dialog.ShowDialog(this);
            }
        }
        catch (Exception ex)
        {
            var dialog = new ConfirmationDialog($"Fehler: {ex.Message}");
            await dialog.ShowDialog(this);
        }
    }

    private async void RemoveAdminButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = AdminSearchResultsList.SelectedItem as EZKPM.Client.Desktop.Services.AdPrincipal;
        if (selected == null) return;

        try
        {
            var req = new SetAdminRequestDto { TargetAdSid = selected.Sid, IsAdmin = false };
            var response = await _apiClient.HttpClient.PostAsJsonAsync("/api/v1/recovery/set-admin", req);
            if (response.IsSuccessStatusCode)
            {
                var dialog = new ConfirmationDialog($"{selected.DisplayName} ist nun KEIN Admin mehr.");
                await dialog.ShowDialog(this);
            }
            else
            {
                string err = await response.Content.ReadAsStringAsync();
                var dialog = new ConfirmationDialog($"Fehler: {err}");
                await dialog.ShowDialog(this);
            }
        }
        catch (Exception ex)
        {
            var dialog = new ConfirmationDialog($"Fehler: {ex.Message}");
            await dialog.ShowDialog(this);
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
}
