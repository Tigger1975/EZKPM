using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using EZKPM.Server.PDP.Hubs;
using EZKPM.Server.PDP.Services;

namespace EZKPM.Server.PDP.Controllers
{
    public class AuthorizationController : Controller
    {
        private readonly IHubContext<ClientSyncHub> _hubContext;
        private readonly PendingAuthStore _authStore;
        private readonly Microsoft.Extensions.Configuration.IConfiguration _config;

        public AuthorizationController(IHubContext<ClientSyncHub> hubContext, PendingAuthStore authStore, Microsoft.Extensions.Configuration.IConfiguration config)
        {
            _hubContext = hubContext;
            _authStore = authStore;
            _config = config;
        }

        [HttpGet("~/connect/authorize")]
        [HttpPost("~/connect/authorize")]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> Authorize()
        {
            var request = HttpContext.GetOpenIddictServerRequest() ??
                          throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

            // 1. Identify User. In a real environment, we use Windows Authentication (Negotiate)
            // If the user isn't authenticated yet, we would normally return Challenge().
            // For now, we simulate that we extracted their SID from the Kerberos ticket.
            string userSid = User.Identity?.IsAuthenticated == true 
                ? (User.FindFirstValue(ClaimTypes.PrimarySid) ?? "Unknown")
                : "S-1-5-21-DUMMY-TEST-USER"; // Mock SID for development

            // 2. Check if the user's Client is connected via SignalR
            var connectionId = ClientSyncHub.GetConnectionIdForSid(userSid);

            // 3. Create a pending Auth Request
            var authReq = _authStore.CreateRequest(userSid, request.ClientId);

            if (string.IsNullOrEmpty(connectionId))
            {
                // The client is not connected to THIS server.
                // Broadcast the AuthRequest to other P2P nodes!
                var peers = _config.GetSection("P2PConfig:Peers").Get<string[]>() ?? Array.Empty<string>();
                var myUrl = _config.GetValue<string>("P2PConfig:LocalUrl") ?? "https://localhost:5001";
                
                using var http = new System.Net.Http.HttpClient();
                var content = new System.Net.Http.StringContent(System.Text.Json.JsonSerializer.Serialize(new {
                    RequestId = authReq.Id,
                    TargetSid = userSid,
                    OriginServerUrl = myUrl,
                    ClientId = request.ClientId
                }), System.Text.Encoding.UTF8, "application/json");

                foreach (var peer in peers)
                {
                    // Fire and forget (don't wait for responses from all peers)
                    _ = http.PostAsync($"{peer.TrimEnd('/')}/api/p2p/auth-ping", content);
                }
            }
            else
            {
                // 4. Send Ping to the locally connected Client
                await _hubContext.Clients.Client(connectionId).SendAsync("PingAuthRequest", new {
                    RequestId = authReq.Id,
                    AppId = request.ClientId,
                    OriginServerUrl = _config.GetValue<string>("P2PConfig:LocalUrl") ?? "https://localhost:5001",
                    Timestamp = DateTime.UtcNow
                });
            }

            // 5. Wait for the Client to approve (Out-of-band Push)
            // Wir warten asynchron, bis der Client via SignalR `SubmitAuthResult` aufruft.
            try
            {
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(60));
                var completedTask = await Task.WhenAny(authReq.Tcs.Task, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    _authStore.CompleteRequest(authReq.Id, false); // Cancel
                    return Forbid(
                        authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                        properties: new AuthenticationProperties(new Dictionary<string, string>
                        {
                            [OpenIddictServerAspNetCoreConstants.Properties.Error] = OpenIddictConstants.Errors.AccessDenied,
                            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "Zeitüberschreitung bei der Bestätigung."
                        }));
                }

                bool isApproved = authReq.Tcs.Task.Result;

                if (!isApproved)
                {
                    return Forbid(
                        authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                        properties: new AuthenticationProperties(new Dictionary<string, string>
                        {
                            [OpenIddictServerAspNetCoreConstants.Properties.Error] = OpenIddictConstants.Errors.AccessDenied,
                            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "Der Benutzer hat die Anmeldung abgelehnt."
                        }));
                }
            }
            catch (Exception ex)
            {
                return BadRequest(new { Error = ex.Message });
            }

            // 6. User Approved -> Issue OIDC Token
            var identity = new ClaimsIdentity(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            identity.AddClaim(OpenIddictConstants.Claims.Subject, userSid);
            // Optionally add more claims here

            var principal = new ClaimsPrincipal(identity);
            principal.SetScopes(request.GetScopes());

            return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        [HttpPost("~/connect/token")]
        public IActionResult Exchange()
        {
            var request = HttpContext.GetOpenIddictServerRequest() ??
                          throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

            if (request.IsAuthorizationCodeGrantType())
            {
                // Authenticate the user from the Authorization Code
                var authenticateResult = HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme).GetAwaiter().GetResult();
                var principal = authenticateResult.Principal;

                return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            }

            throw new InvalidOperationException("The specified grant type is not supported.");
        }
    }
}
