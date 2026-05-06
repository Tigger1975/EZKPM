using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using EZKPM.Server.PDP.Data;
using EZKPM.Shared.Contracts;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace EZKPM.Server.PDP.Controllers
{
    [ApiController]
    [Route("api/v1/[controller]")]
    public class RecoveryController : ControllerBase
    {
        private readonly EzkpmDbContext _db;
        private readonly Microsoft.AspNetCore.SignalR.IHubContext<EZKPM.Server.PDP.Hubs.ClientSyncHub> _hubContext;

        public RecoveryController(EzkpmDbContext db, Microsoft.AspNetCore.SignalR.IHubContext<EZKPM.Server.PDP.Hubs.ClientSyncHub> hubContext)
        {
            _db = db;
            _hubContext = hubContext;
        }

        private string GetUserSid()
        {
            string sid = null;
            // Try to get SID from token
            sid = User.FindFirstValue(System.Security.Claims.ClaimTypes.PrimarySid) ?? User.FindFirstValue("sid");
            
            // Fallback for local testing (Cross-Platform safe)
            if (string.IsNullOrEmpty(sid))
            {
                try 
                {
                    if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                    {
                        sid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? "S-1-5-21-DUMMY-FALLBACK";
                    }
                }
                catch { /* Ignored */ }
            }

            if (string.IsNullOrEmpty(sid)) sid = Environment.UserName; // Linux fallback
            
            return EZKPM.Server.PDP.Services.SidHasher.HashSid(sid);
        }

        [HttpPost("setup")]
        public async Task<IActionResult> SetupRecovery([FromBody] SetupRecoveryDto request)
        {
            var hashedSid = EZKPM.Server.PDP.Services.SidHasher.HashSid(request.AdSid);
            var profile = await _db.UserProfiles.FirstOrDefaultAsync(u => u.AdSid == hashedSid);
            if (profile == null)
            {
                profile = new UserProfile { AdSid = hashedSid };
                _db.UserProfiles.Add(profile);
            }
            
            profile.EncryptedMasterKeyBackup = request.EncryptedMasterKeyBackup;
            await _db.SaveChangesAsync();

            return Ok();
        }

        [HttpPost("request")]
        public async Task<IActionResult> RequestRecovery([FromBody] InitiateRecoveryRequestDto request)
        {
            var hashedSid = EZKPM.Server.PDP.Services.SidHasher.HashSid(request.AdSid);
            var profile = await _db.UserProfiles.FirstOrDefaultAsync(u => u.AdSid == hashedSid);
            if (profile == null) return NotFound("User profile not found.");

            var existingRequest = await _db.VaultRecoveryRequests
                .FirstOrDefaultAsync(r => r.AdSid == hashedSid && !r.IsCompleted);

            Guid currentRequestId;

            if (existingRequest != null)
            {
                existingRequest.EphemeralUserPubKey = request.EphemeralUserPubKey;
                existingRequest.RequestedAt = DateTime.UtcNow; // Reset window
                currentRequestId = existingRequest.Id;
            }
            else
            {
                var newRequest = new VaultRecoveryRequest
                {
                    AdSid = hashedSid,
                    RequesterSid = GetUserSid(), // The admin who initiates the request
                    EphemeralUserPubKey = request.EphemeralUserPubKey,
                    RequiredShares = 2 // 2 OTHER admins required for 6-eyes principle
                };
                _db.VaultRecoveryRequests.Add(newRequest);
                currentRequestId = newRequest.Id;
            }

            _db.RecoveryAuditLogs.Add(new RecoveryAuditLog
            {
                RecoveryRequestId = currentRequestId,
                Action = "Requested",
                ActorSid = hashedSid,
                Details = "User initiated a Vault Recovery request."
            });

            await _db.SaveChangesAsync();

            // Broadcast an die Administratoren über SignalR
            var adminSids = await _db.UserProfiles
                .Where(u => u.IsAdmin)
                .Select(u => u.AdSid)
                .ToListAsync();

            foreach (var adminSid in adminSids)
            {
                var connectionId = EZKPM.Server.PDP.Hubs.ClientSyncHub.GetConnectionIdForSid(adminSid);
                if (!string.IsNullOrEmpty(connectionId))
                {
                    await _hubContext.Clients.Client(connectionId).SendAsync("RecoveryRequested", currentRequestId, hashedSid);
                }
            }

            // Fallback: Zusätzlich ein genereller Broadcast für angemeldete Admins, 
            // deren SID im Hub ggf. nicht exakt registriert wurde.
            await _hubContext.Clients.All.SendAsync("GlobalRecoveryRequested", currentRequestId, hashedSid);

            return Ok();
        }

        [HttpPost("approve")]
        public async Task<IActionResult> ApproveRecovery([FromBody] ProvideRecoveryShareDto request)
        {
            var hashedAdminSid = EZKPM.Server.PDP.Services.SidHasher.HashSid(request.AdminSid);
            var adminProfile = await _db.UserProfiles.FirstOrDefaultAsync(u => u.AdSid == hashedAdminSid);

            if (adminProfile == null || !adminProfile.IsAdmin)
                return Forbid("Only administrators can approve a recovery request.");

            var recovery = await _db.VaultRecoveryRequests
                .Include(r => r.ProvidedShares)
                .FirstOrDefaultAsync(r => r.Id == request.RecoveryRequestId);

            if (recovery == null) return NotFound("Recovery request not found.");
            if (recovery.IsCompleted) return BadRequest("Recovery already completed.");

            if (hashedAdminSid == recovery.RequesterSid)
                return BadRequest("The admin who initiated the recovery request cannot approve it (6-eyes principle requires 2 other admins).");

            // Zeitfenster-Prüfung (z.B. max 1 Stunde)
            if (DateTime.UtcNow > recovery.RequestedAt.AddHours(1))
            {
                _db.RecoveryAuditLogs.Add(new RecoveryAuditLog
                {
                    RecoveryRequestId = recovery.Id,
                    Action = "Expired",
                    ActorSid = "SYSTEM",
                    Details = "Recovery request expired before enough approvals were received."
                });
                
                _db.VaultRecoveryRequests.Remove(recovery);
                await _db.SaveChangesAsync();
                return BadRequest("The recovery request has expired.");
            }

            if (recovery.ProvidedShares.Any(s => s.AdminSid == hashedAdminSid))
                return BadRequest("Admin has already provided a share.");

            var share = new VaultRecoveryShare
            {
                RecoveryRequestId = recovery.Id,
                AdminSid = hashedAdminSid,
                EncryptedShareBlob = request.EncryptedShareBlob
            };
            
            _db.VaultRecoveryShares.Add(share);

            _db.RecoveryAuditLogs.Add(new RecoveryAuditLog
            {
                RecoveryRequestId = recovery.Id,
                Action = "Approved",
                ActorSid = hashedAdminSid,
                Details = "Admin approved the recovery request."
            });

            // Wenn wir durch diese Freigabe das Limit erreichen, schließen wir ab
            if (recovery.ProvidedShares.Count + 1 >= recovery.RequiredShares)
            {
                recovery.IsCompleted = true;
                _db.RecoveryAuditLogs.Add(new RecoveryAuditLog
                {
                    RecoveryRequestId = recovery.Id,
                    Action = "Completed",
                    ActorSid = "SYSTEM",
                    Details = "Recovery threshold met. Fragments released to the user."
                });
            }

            await _db.SaveChangesAsync();

            return Ok();
        }

        [HttpGet("status/{adSid}")]
        public async Task<ActionResult<RecoveryStatusResponseDto>> GetRecoveryStatus(string adSid)
        {
            var hashedSid = EZKPM.Server.PDP.Services.SidHasher.HashSid(adSid);
            var profile = await _db.UserProfiles.FirstOrDefaultAsync(u => u.AdSid == hashedSid);
            if (profile == null) return NotFound("Profile not found.");

            var recovery = await _db.VaultRecoveryRequests
                .Include(r => r.ProvidedShares)
                .Where(r => r.AdSid == hashedSid && !r.IsCompleted)
                .OrderByDescending(r => r.RequestedAt)
                .FirstOrDefaultAsync();

            if (recovery == null) return NotFound("No active recovery request.");

            // Zeitfenster-Prüfung
            if (DateTime.UtcNow > recovery.RequestedAt.AddHours(1))
            {
                _db.RecoveryAuditLogs.Add(new RecoveryAuditLog
                {
                    RecoveryRequestId = recovery.Id,
                    Action = "Expired",
                    ActorSid = "SYSTEM",
                    Details = "Recovery request expired while polling status."
                });
                _db.VaultRecoveryRequests.Remove(recovery);
                await _db.SaveChangesAsync();
                return BadRequest("The recovery request has expired.");
            }

            var response = new RecoveryStatusResponseDto
            {
                RecoveryRequestId = recovery.Id,
                AdSid = recovery.AdSid,
                EncryptedMasterKeyBackup = profile.EncryptedMasterKeyBackup,
                RequiredShares = recovery.RequiredShares,
                IsCompleted = recovery.ProvidedShares.Count >= recovery.RequiredShares,
                EncryptedShareBlobs = recovery.ProvidedShares.Select(s => s.EncryptedShareBlob).ToList()
            };

            return Ok(response);
        }

        [HttpPost("set-admin")]
        public async Task<IActionResult> SetAdmin([FromBody] SetAdminRequestDto request)
        {
            // Security Note: In a real environment, this should be protected by an Authorize attribute
            // requiring an existing Admin or Domain Admin role from the token.
            var hashedTargetSid = EZKPM.Server.PDP.Services.SidHasher.HashSid(request.TargetAdSid);
            var profile = await _db.UserProfiles.FirstOrDefaultAsync(u => u.AdSid == hashedTargetSid);
            
            if (profile == null)
            {
                profile = new UserProfile { AdSid = hashedTargetSid, IsAdmin = request.IsAdmin };
                _db.UserProfiles.Add(profile);
            }
            else
            {
                profile.IsAdmin = request.IsAdmin;
            }

            await _db.SaveChangesAsync();
            return Ok();
        }

        [HttpGet("pending")]
        public async Task<ActionResult<List<VaultRecoveryRequest>>> GetPendingRequests()
        {
            // Bereinige abgelaufene Requests beim Abfragen
            var cutoff = DateTime.UtcNow.AddHours(-1);
            var expired = await _db.VaultRecoveryRequests
                .Where(r => r.RequestedAt < cutoff && !r.IsCompleted)
                .ToListAsync();

            if (expired.Any())
            {
                foreach (var ex in expired)
                {
                    _db.RecoveryAuditLogs.Add(new RecoveryAuditLog
                    {
                        RecoveryRequestId = ex.Id,
                        Action = "Expired",
                        ActorSid = "SYSTEM",
                        Details = "Recovery request expired (1 hour window)."
                    });
                }
                _db.VaultRecoveryRequests.RemoveRange(expired);
                await _db.SaveChangesAsync();
            }

            var pending = await _db.VaultRecoveryRequests
                .Include(r => r.ProvidedShares)
                .Where(r => !r.IsCompleted)
                .OrderByDescending(r => r.RequestedAt)
                .ToListAsync();

            return Ok(pending);
        }
    }

    public class SetAdminRequestDto
    {
        public string TargetAdSid { get; set; }
        public bool IsAdmin { get; set; }
    }
}
