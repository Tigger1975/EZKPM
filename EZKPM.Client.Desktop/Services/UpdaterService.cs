using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Avalonia.Threading;
using EZKPM.Shared.Contracts;
using EZKPM.Client.Desktop.Views;
using System.Reflection;
using System.Security.Principal;
using System.Net.Http.Json;

namespace EZKPM.Client.Desktop.Services
{
    public class UpdaterService : BackgroundService
    {
        private readonly ILogger<UpdaterService> _logger;
        private readonly HttpClient _httpClient;
        private string _serverUrl => ConfigurationManager.CurrentConfig.ServerUrl;

        public UpdaterService(ILogger<UpdaterService> logger)
        {
            _logger = logger;
            var handler = new HttpClientHandler 
            { 
                
                ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
            };
            _httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Initial check after 5 seconds to not block startup
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckForUpdatesAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to check for updates in background loop.");
                }

                // Check every minute (Alpha Phase)
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }

        private static bool HasAdminRights()
        {
            var identity = WindowsIdentity.GetCurrent();
            if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)) return true;
            
            if (identity.Groups != null)
            {
                foreach (var group in identity.Groups)
                {
                    if (group.Value == "S-1-5-32-544") return true;
                }
            }
            return false;
        }

        public static async Task<bool> CheckAndPromptForUpdateAtStartupAsync(Avalonia.Controls.Window parentWindow)
        {
            var serverUrl = ConfigurationManager.CurrentConfig.ServerUrl;
            if (string.IsNullOrEmpty(serverUrl)) return false;

            var currentVersion = (System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(System.Reflection.Assembly.GetExecutingAssembly())?.InformationalVersion ?? "0.0.0.0").Split('+')[0];
            
            var handler = new HttpClientHandler 
            { 
                
                ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
            };
            using var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
            
            try 
            {
                var url = $"{serverUrl}/api/updater/check?currentVersion={currentVersion}";
                var response = await httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode) return false;

                var json = await response.Content.ReadAsStringAsync();
                var updateInfo = JsonSerializer.Deserialize<UpdateCheckResponseDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (updateInfo != null && updateInfo.UpdateAvailable)
                {
                    bool isMachineWide = AppContext.BaseDirectory.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), StringComparison.OrdinalIgnoreCase);
                    bool hasAdmin = HasAdminRights();

                    if (isMachineWide && !hasAdmin)
                    {
                        var msgBox = new ConfirmationDialog($"Ein kritisches Update (v{updateInfo.LatestVersion}) ist verfügbar.\n\nDa EZKPM unter Programme installiert ist, fehlen Ihnen die Administrator-Rechte für ein Auto-Update. Bitte kontaktieren Sie Ihre IT.");
                        await msgBox.ShowDialogAsync(parentWindow);
                        return false; 
                    }

                    var updatePrompt = new ConfirmationDialog($"Ein Update auf Version {updateInfo.LatestVersion} ist verfügbar.\n\nRelease Notes:\n{updateInfo.ReleaseNotes}\n\nMöchten Sie das Update jetzt durchführen?");
                    bool doUpdate = await updatePrompt.ShowDialogAsync(parentWindow);
                    
                    if (doUpdate)
                    {
                        var updaterService = new UpdaterService(new Microsoft.Extensions.Logging.Abstractions.NullLogger<UpdaterService>());
                        await updaterService.PerformUpdateAsync(updateInfo.DownloadUrl);
                        return true; 
                    }
                }
            }
            catch { }
            return false;
        }

        public async Task CheckForUpdatesAsync(CancellationToken ct, bool forceShow = false)
        {
            var currentVersion = (System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(System.Reflection.Assembly.GetExecutingAssembly())?.InformationalVersion ?? "0.0.0.0").Split('+')[0];
            
            var url = $"{_serverUrl}/api/updater/check?currentVersion={currentVersion}";
            var response = await _httpClient.GetAsync(url, ct);
            
            if (!response.IsSuccessStatusCode) return;

            var json = await response.Content.ReadAsStringAsync(ct);
            var updateInfo = JsonSerializer.Deserialize<UpdateCheckResponseDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (updateInfo != null && updateInfo.UpdateAvailable)
            {
                _logger.LogInformation($"Update {updateInfo.LatestVersion} is available.");
                
                Dispatcher.UIThread.Post(() =>
                {
                    PromptForUpdate(updateInfo, forceShow);
                });
            }
        }

        private static bool _isUpdatePromptShown = false;

        private void PromptForUpdate(UpdateCheckResponseDto updateInfo, bool forceShow = false)
        {
            if (_isUpdatePromptShown && !forceShow) return;
            _isUpdatePromptShown = true;
            var currentDir = AppContext.BaseDirectory;
            bool isMachineWide = currentDir.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), StringComparison.OrdinalIgnoreCase);
            bool hasAdmin = HasAdminRights();

            var app = (App)Avalonia.Application.Current;
            var mainWindow = (Avalonia.Controls.Window)((Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)app.ApplicationLifetime).MainWindow;

            // Scneario B & C: Machine Wide without admin rights
            if (isMachineWide && !hasAdmin)
            {
                var msgBox = new ConfirmationDialog($"Ein kritisches Update (v{updateInfo.LatestVersion}) ist verfügbar.\n\nDa EZKPM unter Programme installiert ist, fehlen Ihnen die Administrator-Rechte für ein Auto-Update. Bitte kontaktieren Sie Ihre IT.");
                _ = msgBox.ShowDialogAsync(mainWindow);
                return;
            }

            // Scenario A: Per-User or Admin
            var updatePrompt = new ConfirmationDialog($"Ein Update auf Version {updateInfo.LatestVersion} ist verfügbar.\n\nRelease Notes:\n{updateInfo.ReleaseNotes}\n\nMöchten Sie das Update jetzt durchführen?");
            _ = Task.Run(async () =>
            {
                bool result = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => updatePrompt.ShowDialogAsync(mainWindow));
                if (result)
                {
                    await PerformUpdateAsync(updateInfo.DownloadUrl);
                }
                else
                {
                    _isUpdatePromptShown = false; // Allow showing again later if needed, or we keep it false so it never annoys them again until restart?
                    // Actually, if we don't reset it, it won't prompt again during this session, which is usually exactly what the user wants!
                }
            });
        }

        public async Task PerformUpdateAsync(string downloadUrl)
        {
            try
            {
                // Ensure absolute URL
                if (downloadUrl.StartsWith("/"))
                {
                    downloadUrl = _serverUrl + downloadUrl;
                }

                var tempZipPath = Path.Combine(Path.GetTempPath(), "EZKPM_Update.zip");
                
                var response = await _httpClient.GetAsync(downloadUrl);
                response.EnsureSuccessStatusCode();

                using (var fs = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write))
                {
                    await response.Content.CopyToAsync(fs);
                }

                var currentDir = AppContext.BaseDirectory;
                var currentExe = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(currentExe)) currentExe = Path.Combine(currentDir, "EZKPM.Client.Desktop.exe");

                // Create Updater Script
                var scriptPath = Path.Combine(Path.GetTempPath(), "EZKPM_Updater.ps1");
                var scriptContent = $@"
param()
Start-Sleep -Seconds 3
Write-Host 'Entpacke Update...'
Expand-Archive -Path '{tempZipPath}' -DestinationPath '{currentDir}' -Force
Remove-Item -Path '{tempZipPath}' -Force
Write-Host 'Starte EZKPM neu...'
Start-Process -FilePath '{currentExe}'
";
                File.WriteAllText(scriptPath, scriptContent);

                // Run the script in a detached process
                var psInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
                    UseShellExecute = true,
                    CreateNoWindow = true
                };
                
                bool isMachineWide = AppContext.BaseDirectory.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), StringComparison.OrdinalIgnoreCase);
                if (isMachineWide)
                {
                    psInfo.Verb = "runas";
                }
                
                Process.Start(psInfo);

                // Exit current process so files can be overwritten
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fehler beim Herunterladen oder Anwenden des Updates.");
                var msgBox = new ConfirmationDialog("Fehler beim Update: " + ex.Message);
                msgBox.Show();
            }
        }
    }
}
