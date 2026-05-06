using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace EZKPM.Server.PDP.Services
{
    public class PendingAuthStore
    {
        public class AuthRequest
        {
            public string Id { get; set; } = Guid.NewGuid().ToString();
            public string TargetSid { get; set; }
            public string ClientAppId { get; set; }
            public string OriginServerUrl { get; set; } // For P2P forwarding
            public TaskCompletionSource<bool> Tcs { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddMinutes(5);
        }

        private readonly ConcurrentDictionary<string, AuthRequest> _requests = new();

        public AuthRequest CreateRequest(string sid, string clientId, string originServerUrl = null)
        {
            var req = new AuthRequest { TargetSid = sid, ClientAppId = clientId, OriginServerUrl = originServerUrl };
            _requests[req.Id] = req;
            return req;
        }

        public AuthRequest GetRequest(string id)
        {
            _requests.TryGetValue(id, out var req);
            return req;
        }

        public bool CompleteRequest(string id, bool isApproved)
        {
            if (_requests.TryRemove(id, out var req))
            {
                req.Tcs.TrySetResult(isApproved);
                return true;
            }
            return false;
        }
    }
}
