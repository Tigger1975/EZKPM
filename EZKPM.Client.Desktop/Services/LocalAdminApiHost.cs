using System;
using System.IO;
using System.Net;
using System.Net.Http.Json;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EZKPM.Client.Desktop.Services
{
    public class LocalAdminApiHost
    {
        private HttpListener _listener;
        private CancellationTokenSource _cts;
        private Task _listenTask;

        public class InviteRequest
        {
            public string Sid { get; set; }
            public string SamAccountName { get; set; }
        }

        public Task StartAsync(int port, string allowedSid, EZKPM.Client.Core.Services.VaultApiClient apiClient, Func<Task> syncAction)
        {
            if (_listener != null) return Task.CompletedTask;

            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{port}/api/admin/");
            // Enforce Windows Authentication
            _listener.AuthenticationSchemes = AuthenticationSchemes.Negotiate;

            try
            {
                _listener.Start();
                _cts = new CancellationTokenSource();
                _listenTask = ListenLoop(allowedSid, apiClient, syncAction, _cts.Token);
                Console.WriteLine($"Local Admin API listening on http://localhost:{port}/api/admin/");
            }
            catch (Exception ex)
            {
                Program.LogDebug($"Failed to start LocalAdminApiHost: {ex.Message}");
            }

            return Task.CompletedTask;
        }

        private async Task ListenLoop(string allowedSid, EZKPM.Client.Core.Services.VaultApiClient apiClient, Func<Task> syncAction, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    _ = ProcessRequestAsync(context, allowedSid, apiClient, syncAction);
                }
                catch (HttpListenerException) when (token.IsCancellationRequested)
                {
                    // Expected on stop
                    break;
                }
                catch (Exception ex)
                {
                    Program.LogDebug($"LocalAdminApiHost loop error: {ex.Message}");
                }
            }
        }

        private async Task ProcessRequestAsync(HttpListenerContext context, string allowedSid, EZKPM.Client.Core.Services.VaultApiClient apiClient, Func<Task> syncAction)
        {
            var request = context.Request;
            var response = context.Response;

            try
            {
                // Verify SID
                if (context.User?.Identity is WindowsIdentity winIdentity)
                {
                    if (!string.IsNullOrEmpty(allowedSid))
                    {
                        var callerSid = winIdentity.User?.Value;
                        if (callerSid != allowedSid)
                        {
                            await SendResponseAsync(response, 403, "Forbidden: SID mismatch.");
                            return;
                        }
                    }
                }
                else
                {
                    await SendResponseAsync(response, 401, "Unauthorized");
                    return;
                }

                string path = request.Url?.AbsolutePath.TrimEnd('/');
                string method = request.HttpMethod;

                if (method == "GET" && path == "/api/admin/ping")
                {
                    var resObj = new { Status = "Online", Message = "EZKPM Local Admin API is running." };
                    await SendJsonResponseAsync(response, 200, resObj);
                    return;
                }

                if (method == "POST" && path == "/api/admin/sync")
                {
                    if (syncAction != null)
                    {
                        await syncAction();
                        await SendJsonResponseAsync(response, 200, new { Status = "Success", Message = "AD Group Sync completed." });
                    }
                    else
                    {
                        await SendResponseAsync(response, 500, "Sync action not configured.");
                    }
                    return;
                }

                if (method == "POST" && path == "/api/admin/invite")
                {
                    using var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8);
                    string body = await reader.ReadToEndAsync();
                    
                    var reqData = JsonSerializer.Deserialize<InviteRequest>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (reqData == null || string.IsNullOrWhiteSpace(reqData.Sid) || string.IsNullOrWhiteSpace(reqData.SamAccountName))
                    {
                        await SendResponseAsync(response, 400, "Sid and SamAccountName are required.");
                        return;
                    }

                    using var sha256 = System.Security.Cryptography.SHA256.Create();
                    var sidHash = Convert.ToBase64String(sha256.ComputeHash(Encoding.UTF8.GetBytes(reqData.Sid)));
                    var nameHash = Convert.ToBase64String(sha256.ComputeHash(Encoding.UTF8.GetBytes(reqData.SamAccountName)));

                    var payload = new { HashedSid = sidHash, HashedUsername = nameHash };
                    var apiResp = await apiClient.HttpClient.PostAsJsonAsync("/api/v1/auth/invite", payload);

                    if (apiResp.IsSuccessStatusCode)
                    {
                        await SendJsonResponseAsync(response, 200, new { Status = "Success", Message = "User invited successfully." });
                        return;
                    }
                    else if (apiResp.StatusCode == HttpStatusCode.Conflict)
                    {
                        await SendResponseAsync(response, 409, "User is already invited or active.");
                        return;
                    }

                    await SendResponseAsync(response, (int)apiResp.StatusCode, "Upstream API error.");
                    return;
                }

                await SendResponseAsync(response, 404, "Not Found");
            }
            catch (Exception ex)
            {
                await SendResponseAsync(response, 500, $"Internal Server Error: {ex.Message}");
            }
        }

        private async Task SendResponseAsync(HttpListenerResponse response, int statusCode, string text)
        {
            response.StatusCode = statusCode;
            response.ContentType = "text/plain";
            var buffer = Encoding.UTF8.GetBytes(text);
            response.ContentLength64 = buffer.Length;
            try
            {
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                response.OutputStream.Close();
            }
            catch { }
        }

        private async Task SendJsonResponseAsync(HttpListenerResponse response, int statusCode, object data)
        {
            response.StatusCode = statusCode;
            response.ContentType = "application/json";
            var json = JsonSerializer.Serialize(data);
            var buffer = Encoding.UTF8.GetBytes(json);
            response.ContentLength64 = buffer.Length;
            try
            {
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                response.OutputStream.Close();
            }
            catch { }
        }

        public Task StopAsync()
        {
            if (_listener != null)
            {
                _cts?.Cancel();
                _listener.Stop();
                _listener.Close();
                _listener = null;
            }
            return Task.CompletedTask;
        }
    }
}
