using System;
using System.Threading.Tasks;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authentication.Negotiate;
using System.Security.Principal;

namespace EZKPM.Client.Desktop.Services
{
    public class LocalAdminApiHost
    {
        private WebApplication _app;

        public async Task StartAsync(int port, string allowedSid, EZKPM.Client.Core.Services.VaultApiClient apiClient)
        {
            if (_app != null) return;

            var builder = WebApplication.CreateBuilder();

            // Setup Kestrel to listen only on localhost
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.ListenLocalhost(port);
            });

            // Enable Windows Authentication (Negotiate / NTLM)
            builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme)
                .AddNegotiate();
            builder.Services.AddAuthorization(options =>
            {
                options.FallbackPolicy = options.DefaultPolicy;
            });

            _app = builder.Build();

            _app.UseAuthentication();
            _app.UseAuthorization();

            // Middleware for strict SID checking
            _app.Use(async (context, next) =>
            {
                if (context.User.Identity is WindowsIdentity windowsIdentity)
                {
                    if (!string.IsNullOrEmpty(allowedSid))
                    {
                        var callerSid = windowsIdentity.User?.Value;
                        if (callerSid != allowedSid)
                        {
                            context.Response.StatusCode = 403;
                            await context.Response.WriteAsync("Forbidden: SID mismatch.");
                            return;
                        }
                    }
                }
                else
                {
                    context.Response.StatusCode = 401;
                    return;
                }
                
                await next();
            });

            // Simple test endpoint
            _app.MapGet("/api/admin/ping", () => Results.Ok(new { Status = "Online", Message = "EZKPM Local Admin API is running." }));

            // Invite User Endpoint
            _app.MapPost("/api/admin/invite", async (InviteRequest req) =>
            {
                if (string.IsNullOrWhiteSpace(req.Sid) || string.IsNullOrWhiteSpace(req.SamAccountName))
                    return Results.BadRequest("Sid and SamAccountName are required.");

                using var sha256 = System.Security.Cryptography.SHA256.Create();
                var sidHash = Convert.ToBase64String(sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(req.Sid)));
                var nameHash = Convert.ToBase64String(sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(req.SamAccountName)));

                var payload = new { HashedSid = sidHash, HashedUsername = nameHash };
                var response = await apiClient.HttpClient.PostAsJsonAsync("/api/v1/auth/invite", payload);

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
                    if (result.TryGetProperty("pairingCode", out var codeElement))
                    {
                        return Results.Ok(new { PairingCode = codeElement.GetString() });
                    }
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
                {
                    return Results.Conflict("User is already invited or active.");
                }

                return Results.StatusCode((int)response.StatusCode);
            });

            await _app.StartAsync();
            Console.WriteLine($"Local Admin API listening on http://localhost:{port}");
        }

        public class InviteRequest
        {
            public string Sid { get; set; }
            public string SamAccountName { get; set; }
        }

        public async Task StopAsync()
        {
            if (_app != null)
            {
                await _app.StopAsync();
                await _app.DisposeAsync();
                _app = null;
            }
        }
    }
}
