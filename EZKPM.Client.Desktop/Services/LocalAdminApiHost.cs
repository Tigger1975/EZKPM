using System;
using System.Threading.Tasks;
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

        public async Task StartAsync(int port = 5050, string allowedSid = null)
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

            await _app.StartAsync();
            Console.WriteLine($"Local Admin API listening on http://localhost:{port}");
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
