using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace EZKPM.Server.PDP.Hubs
{
    public class ClientSyncHub : Hub
    {
        private readonly ILogger<ClientSyncHub> _logger;
        private readonly Services.PendingAuthStore _authStore;
        
        // Maps ConnectionId -> AD SID
        private static readonly ConcurrentDictionary<string, string> _connectedClients = new();

        public ClientSyncHub(ILogger<ClientSyncHub> logger, Services.PendingAuthStore authStore)
        {
            _logger = logger;
            _authStore = authStore;
        }

        public override Task OnConnectedAsync()
        {
            // For now, we assume the client passes a ?sid=... query parameter 
            // In production, this should be validated via a secure token.
            var httpContext = Context.GetHttpContext();
            var sid = httpContext?.Request.Query["sid"].ToString();
            
            if (!string.IsNullOrEmpty(sid))
            {
                _connectedClients[Context.ConnectionId] = sid;
                _logger.LogInformation($"Client connected: {sid} (Connection: {Context.ConnectionId})");
            }

            return base.OnConnectedAsync();
        }

        public override Task OnDisconnectedAsync(Exception exception)
        {
            if (_connectedClients.TryRemove(Context.ConnectionId, out var sid))
            {
                _logger.LogInformation($"Client disconnected: {sid} (Connection: {Context.ConnectionId})");
            }
            return base.OnDisconnectedAsync(exception);
        }

        // The client calls this method when the user approves/denies the push request
        public async Task SubmitAuthResult(string authRequestId, bool isApproved)
        {
            if (_connectedClients.TryGetValue(Context.ConnectionId, out var sid))
            {
                _logger.LogInformation($"Received Auth Result for Request {authRequestId} from {sid}: Approved={isApproved}");
                
                var req = _authStore.GetRequest(authRequestId);
                if (req != null && !string.IsNullOrEmpty(req.OriginServerUrl))
                {
                    // Forward it back to Server A
                    _logger.LogInformation($"Forwarding Auth Result back to Origin Server: {req.OriginServerUrl}");
                    using var http = new System.Net.Http.HttpClient();
                    var content = new System.Net.Http.StringContent(System.Text.Json.JsonSerializer.Serialize(new {
                        RequestId = authRequestId,
                        IsApproved = isApproved
                    }), System.Text.Encoding.UTF8, "application/json");
                    
                    await http.PostAsync($"{req.OriginServerUrl.TrimEnd('/')}/api/p2p/auth-reply", content);
                    _authStore.CompleteRequest(authRequestId, isApproved); // Clean up local copy
                }
                else
                {
                    // Complete local task
                    _authStore.CompleteRequest(authRequestId, isApproved);
                }
            }
        }
        
        public static string GetConnectionIdForSid(string sid)
        {
            foreach (var kvp in _connectedClients)
            {
                if (kvp.Value == sid) return kvp.Key;
            }
            return null;
        }
    }
}
