using System;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Threading;
using Microsoft.AspNetCore.SignalR.Client;

namespace EZKPM.Client.Desktop.Services
{
    public class SsoSyncClient
    {
        private HubConnection _connection;
        private readonly Func<string, string, string, Task<bool>> _showApprovalDialogFunc;

        public SsoSyncClient(Func<string, string, string, Task<bool>> showApprovalDialogFunc)
        {
            _showApprovalDialogFunc = showApprovalDialogFunc;
        }

        public async Task ConnectAsync(string serverUrl, string userSid)
        {
            if (_connection != null)
            {
                await _connection.DisposeAsync();
            }

            var hubUrl = $"{serverUrl.TrimEnd('/')}/hubs/sync?sid={Uri.EscapeDataString(userSid)}";

            _connection = new HubConnectionBuilder()
                .WithUrl(hubUrl)
                .WithAutomaticReconnect()
                .Build();

            _connection.On<JsonElement>("PingAuthRequest", async (payload) =>
            {
                string requestId = payload.GetProperty("RequestId").GetString();
                string appId = payload.GetProperty("AppId").GetString();
                string originServerUrl = payload.TryGetProperty("OriginServerUrl", out var oUrl) ? oUrl.GetString() : serverUrl;

                // Bring this to the UI Thread to show the Dialog
                bool isApproved = await _showApprovalDialogFunc(requestId, appId, originServerUrl);

                if (_connection.State == HubConnectionState.Connected)
                {
                    await _connection.InvokeAsync("SubmitAuthResult", requestId, isApproved);
                }
            });

            _connection.On<string, string>("RecoveryRequested", (requestId, requesterSid) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    // Basic notification for Admins
                    var notifyIcon = new System.Windows.Forms.NotifyIcon
                    {
                        Icon = System.Drawing.SystemIcons.Warning,
                        Visible = true,
                        BalloonTipTitle = "EZKPM Notfall-Recovery",
                        BalloonTipText = $"Neuer Recovery Request von SID {requesterSid}."
                    };
                    notifyIcon.ShowBalloonTip(5000);
                });
            });

            _connection.On<string, string>("GlobalRecoveryRequested", (requestId, requesterSid) =>
            {
                // Only act if the current user is an Admin (can be checked via local rights)
                // For now, we show the same notification
                Dispatcher.UIThread.Post(() =>
                {
                    var notifyIcon = new System.Windows.Forms.NotifyIcon
                    {
                        Icon = System.Drawing.SystemIcons.Warning,
                        Visible = true,
                        BalloonTipTitle = "EZKPM Notfall-Recovery (Global)",
                        BalloonTipText = $"Neuer Recovery Request von SID {requesterSid}."
                    };
                    notifyIcon.ShowBalloonTip(5000);
                });
            });

            try
            {
                await _connection.StartAsync();
                Console.WriteLine($"SSO Sync Client connected to {hubUrl}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to connect SSO Sync Client: {ex.Message}");
            }
        }
    }
}
