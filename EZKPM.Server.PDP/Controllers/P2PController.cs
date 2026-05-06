using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using EZKPM.Server.PDP.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EZKPM.Server.PDP.Controllers
{
    [ApiController]
    [Route("api/p2p")]
    public class P2PController : ControllerBase
    {
        private readonly EzkpmDbContext _db;
        private readonly IConfiguration _config;
        private readonly ILogger<P2PController> _logger;
        private readonly Services.PendingAuthStore _authStore;
        private readonly Microsoft.AspNetCore.SignalR.IHubContext<Hubs.ClientSyncHub> _hubContext;

        public P2PController(EzkpmDbContext db, IConfiguration config, ILogger<P2PController> logger, Services.PendingAuthStore authStore, Microsoft.AspNetCore.SignalR.IHubContext<Hubs.ClientSyncHub> hubContext)
        {
            _db = db;
            _config = config;
            _logger = logger;
            _authStore = authStore;
            _hubContext = hubContext;
        }

        [HttpPost("push")]
        public async Task<IActionResult> PushUpdates([FromBody] JsonElement payload)
        {
            // 1. P2P Token Authentication
            var expectedToken = _config.GetValue<string>("P2PConfig:P2PToken");
            if (string.IsNullOrEmpty(expectedToken)) return Forbid();

            string authHeader = Request.Headers["Authorization"].ToString();
            if (!authHeader.StartsWith("Bearer ") || authHeader.Substring(7) != expectedToken)
            {
                _logger.LogWarning("Unauthorized P2P sync attempt.");
                return Unauthorized();
            }

            try
            {
                string originNodeId = payload.GetProperty("OriginNodeId").GetString();
                _logger.LogInformation($"Received P2P sync payload from {originNodeId}");

                var assetsProp = payload.GetProperty("Assets");
                var assets = JsonSerializer.Deserialize<List<VaultAsset>>(assetsProp.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                var logsProp = payload.GetProperty("Logs");
                var logs = JsonSerializer.Deserialize<List<AuditLog>>(logsProp.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                // 2. Process Assets (LWW Resolution)
                if (assets != null)
                {
                    foreach (var incomingAsset in assets)
                    {
                        var localAsset = await _db.VaultAssets.Include(a => a.Acls).FirstOrDefaultAsync(a => a.Id == incomingAsset.Id);
                        if (localAsset == null)
                        {
                            // New Asset
                            _db.VaultAssets.Add(incomingAsset);
                        }
                        else
                        {
                            // Conflict Resolution: Last Writer Wins (LWW)
                            if (incomingAsset.UpdatedUtc > localAsset.UpdatedUtc)
                            {
                                localAsset.CipherBlob = incomingAsset.CipherBlob;
                                localAsset.Nonce = incomingAsset.Nonce;
                                localAsset.MetadataHash = incomingAsset.MetadataHash;
                                localAsset.ExpiresAt = incomingAsset.ExpiresAt;
                                localAsset.IsDeleted = incomingAsset.IsDeleted;
                                localAsset.UpdatedUtc = incomingAsset.UpdatedUtc;

                                // Sync ACLs
                                _db.AssetAcls.RemoveRange(localAsset.Acls);
                                foreach (var acl in incomingAsset.Acls)
                                {
                                    acl.Asset = null; // Detach nav prop
                                    localAsset.Acls.Add(acl);
                                }
                            }
                        }
                    }
                }

                // 3. Process Audit Logs
                if (logs != null)
                {
                    foreach (var incomingLog in logs)
                    {
                        bool exists = await _db.AuditLogs.AnyAsync(l => l.Id == incomingLog.Id);
                        if (!exists)
                        {
                            incomingLog.Asset = null; // Prevent entity tracking issues
                            _db.AuditLogs.Add(incomingLog);
                        }
                    }
                }

                await _db.SaveChangesAsync();
                return Ok(new { status = "synced" });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to process P2P push: {ex.Message}");
                return StatusCode(500, ex.Message);
            }
        }

        [HttpGet("pull")]
        public async Task<IActionResult> PullUpdates([FromQuery] DateTime since)
        {
            var expectedToken = _config.GetValue<string>("P2PConfig:P2PToken");
            if (string.IsNullOrEmpty(expectedToken)) return Forbid();

            string authHeader = Request.Headers["Authorization"].ToString();
            if (!authHeader.StartsWith("Bearer ") || authHeader.Substring(7) != expectedToken)
                return Unauthorized();

            var newAssets = await _db.VaultAssets
                .Include(a => a.Acls)
                .Where(a => a.UpdatedUtc > since)
                .ToListAsync();

            var newLogs = await _db.AuditLogs
                .Where(l => l.Timestamp > since)
                .ToListAsync();

            return Ok(new
            {
                OriginNodeId = _config.GetValue<string>("P2PConfig:NodeId"),
                Assets = newAssets,
                Logs = newLogs
            });
        }

        [HttpPost("auth-ping")]
        public async Task<IActionResult> ReceiveAuthPing([FromBody] JsonElement payload)
        {
            // Authenticate token here in a real scenario
            string requestId = payload.GetProperty("RequestId").GetString();
            string targetSid = payload.GetProperty("TargetSid").GetString();
            string originServerUrl = payload.GetProperty("OriginServerUrl").GetString();
            string clientId = payload.GetProperty("ClientId").GetString();

            _logger.LogInformation($"Received Auth-Ping for User {targetSid} from {originServerUrl}");

            var connectionId = Hubs.ClientSyncHub.GetConnectionIdForSid(targetSid);
            if (!string.IsNullOrEmpty(connectionId))
            {
                // The client is connected here! Push to client!
                // We add it to our auth store temporarily, but flag it as a forwarded request
                _authStore.CreateRequest(targetSid, clientId).Id = requestId; // Force ID match

                await _hubContext.Clients.Client(connectionId).SendAsync("PingAuthRequest", new {
                    RequestId = requestId,
                    AppId = clientId,
                    OriginServerUrl = originServerUrl,
                    Timestamp = DateTime.UtcNow
                });
                
                return Ok(new { found = true });
            }

            return Ok(new { found = false });
        }

        [HttpPost("auth-reply")]
        public IActionResult ReceiveAuthReply([FromBody] JsonElement payload)
        {
            // The Client answered to Server C. Server C forwarded it back to us (Server A).
            string requestId = payload.GetProperty("RequestId").GetString();
            bool isApproved = payload.GetProperty("IsApproved").GetBoolean();

            _logger.LogInformation($"Received Auth-Reply from Mesh for Request {requestId}: Approved={isApproved}");
            
            // Complete the local task waiting in AuthorizationController
            bool completed = _authStore.CompleteRequest(requestId, isApproved);
            
            return Ok(new { Success = completed });
        }
    }
}
