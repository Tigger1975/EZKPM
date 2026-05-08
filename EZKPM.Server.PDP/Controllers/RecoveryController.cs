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

            // Get Requester's PersonId
            var requesterProfile = await _db.UserProfiles.FirstOrDefaultAsync(u => u.AdSid == recovery.RequesterSid);
            if (requesterProfile != null && adminProfile.PersonId == requesterProfile.PersonId)
            {
                return BadRequest("The physical person who initiated the recovery request cannot approve it, even using a different linked account (6-eyes principle requires 2 distinct persons).");
            }

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

            // Check if this PersonId has already provided a share
            var existingSharePersonIds = await _db.VaultRecoveryShares
                .Where(s => s.RecoveryRequestId == recovery.Id)
                .Join(_db.UserProfiles, s => s.AdminSid, u => u.AdSid, (s, u) => u.PersonId)
                .ToListAsync();

            if (existingSharePersonIds.Contains(adminProfile.PersonId))
                return BadRequest("A linked account belonging to you has already provided a share.");

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

        [HttpGet("admin-status")]
        public async Task<IActionResult> GetAdminStatus()
        {
            var callerHashedSid = GetUserSid();
            var callerProfile = await _db.UserProfiles.FirstOrDefaultAsync(u => u.AdSid == callerHashedSid);
            bool isCallerAdmin = callerProfile?.IsAdmin == true;
            bool anyAdminExists = await _db.UserProfiles.AnyAsync(u => u.IsAdmin);

            return Ok(new 
            { 
                IsAdmin = isCallerAdmin, 
                IsBootstrapActive = !anyAdminExists,
                HasAccessToAdminPanel = isCallerAdmin || !anyAdminExists
            });
        }

        [HttpPost("filter-admins")]
        public async Task<IActionResult> FilterAdmins([FromBody] System.Collections.Generic.List<string> candidateSids)
        {
            var callerHashedSid = GetUserSid();
            var callerProfile = await _db.UserProfiles.FirstOrDefaultAsync(u => u.AdSid == callerHashedSid);
            bool isCallerAdmin = callerProfile?.IsAdmin == true;
            bool anyAdminExists = await _db.UserProfiles.AnyAsync(u => u.IsAdmin);

            if (!isCallerAdmin && anyAdminExists)
            {
                return Forbid("Only administrators can list admins.");
            }

            var adminSidsInDb = await _db.UserProfiles
                .Where(u => u.IsAdmin)
                .Select(u => u.AdSid)
                .ToListAsync();

            var matchedSids = new System.Collections.Generic.List<string>();

            foreach (var sid in candidateSids)
            {
                var hashed = EZKPM.Server.PDP.Services.SidHasher.HashSid(sid);
                if (adminSidsInDb.Contains(hashed))
                {
                    matchedSids.Add(sid);
                }
            }

            return Ok(matchedSids);
        }

        [HttpPost("set-admin")]
        public async Task<IActionResult> SetAdmin([FromBody] SetAdminRequestDto request)
        {
            var callerHashedSid = GetUserSid();
            var callerProfile = await _db.UserProfiles.FirstOrDefaultAsync(u => u.AdSid == callerHashedSid);
            bool isCallerAdmin = callerProfile?.IsAdmin == true;

            // Bootstrap Logic: Check if ANY admin exists in the entire database
            bool anyAdminExists = await _db.UserProfiles.AnyAsync(u => u.IsAdmin);

            if (anyAdminExists && !isCallerAdmin)
            {
                return Forbid("Only existing administrators can modify admin rights.");
            }

            var hashedTargetSid = EZKPM.Server.PDP.Services.SidHasher.HashSid(request.TargetAdSid);

            // Prevent removing the last admin
            if (!request.IsAdmin && anyAdminExists)
            {
                int adminCount = await _db.UserProfiles.CountAsync(u => u.IsAdmin);
                var targetProfile = await _db.UserProfiles.FirstOrDefaultAsync(u => u.AdSid == hashedTargetSid);
                if (targetProfile != null && targetProfile.IsAdmin && adminCount <= 1)
                {
                    return BadRequest("Cannot remove the last administrator from the system.");
                }
            }

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

        [HttpPost("link-person")]
        public async Task<IActionResult> LinkPerson([FromBody] LinkPersonDto request)
        {
            var callerHashedSid = GetUserSid();
            var callerProfile = await _db.UserProfiles.FirstOrDefaultAsync(u => u.AdSid == callerHashedSid);
            
            if (callerProfile == null || !callerProfile.IsAdmin)
                return Forbid("Only administrators can link user accounts.");

            var hashedSourceSid = EZKPM.Server.PDP.Services.SidHasher.HashSid(request.SourceAdSid);
            var hashedTargetSid = EZKPM.Server.PDP.Services.SidHasher.HashSid(request.TargetAdSid);

            var sourceProfile = await _db.UserProfiles.FirstOrDefaultAsync(u => u.AdSid == hashedSourceSid);
            var targetProfile = await _db.UserProfiles.FirstOrDefaultAsync(u => u.AdSid == hashedTargetSid);

            // Auto-create empty profiles if they don't exist yet, so they can be "hijacked" upon first login
            if (sourceProfile == null)
            {
                sourceProfile = new UserProfile { AdSid = hashedSourceSid, EncryptedMasterKeyBackup = "" };
                _db.UserProfiles.Add(sourceProfile);
            }
            if (targetProfile == null)
            {
                targetProfile = new UserProfile { AdSid = hashedTargetSid, EncryptedMasterKeyBackup = "" };
                _db.UserProfiles.Add(targetProfile);
            }

            // Assign the target's PersonId to the source, linking them physically
            sourceProfile.PersonId = targetProfile.PersonId;
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
}
