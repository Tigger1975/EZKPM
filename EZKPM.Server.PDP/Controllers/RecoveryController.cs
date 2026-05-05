using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using EZKPM.Server.PDP.Data;
using EZKPM.Shared.Contracts;

namespace EZKPM.Server.PDP.Controllers
{
    [ApiController]
    [Route("api/v1/[controller]")]
    public class RecoveryController : ControllerBase
    {
        private readonly EzkpmDbContext _db;

        public RecoveryController(EzkpmDbContext db)
        {
            _db = db;
        }

        [HttpPost("setup")]
        public async Task<IActionResult> SetupRecovery([FromBody] SetupRecoveryDto request)
        {
            var profile = await _db.UserProfiles.FirstOrDefaultAsync(u => u.AdSid == request.AdSid);
            if (profile == null)
            {
                profile = new UserProfile { AdSid = request.AdSid };
                _db.UserProfiles.Add(profile);
            }
            
            profile.EncryptedMasterKeyBackup = request.EncryptedMasterKeyBackup;
            await _db.SaveChangesAsync();

            return Ok();
        }

        [HttpPost("request")]
        public async Task<IActionResult> RequestRecovery([FromBody] InitiateRecoveryRequestDto request)
        {
            var profile = await _db.UserProfiles.FirstOrDefaultAsync(u => u.AdSid == request.AdSid);
            if (profile == null) return NotFound("User profile not found.");

            var existingRequest = await _db.VaultRecoveryRequests
                .FirstOrDefaultAsync(r => r.AdSid == request.AdSid && !r.IsCompleted);

            if (existingRequest != null)
            {
                // Update ephemeral key if they request again
                existingRequest.EphemeralUserPubKey = request.EphemeralUserPubKey;
            }
            else
            {
                var newRequest = new VaultRecoveryRequest
                {
                    AdSid = request.AdSid,
                    EphemeralUserPubKey = request.EphemeralUserPubKey,
                    RequiredShares = 2 // Hardcoded to 2-of-N for this prototype
                };
                _db.VaultRecoveryRequests.Add(newRequest);
            }

            await _db.SaveChangesAsync();
            return Ok();
        }

        [HttpPost("approve")]
        public async Task<IActionResult> ApproveRecovery([FromBody] ProvideRecoveryShareDto request)
        {
            var recovery = await _db.VaultRecoveryRequests
                .Include(r => r.ProvidedShares)
                .FirstOrDefaultAsync(r => r.Id == request.RecoveryRequestId);

            if (recovery == null) return NotFound("Recovery request not found.");
            if (recovery.IsCompleted) return BadRequest("Recovery already completed.");

            // Avoid duplicate shares from the same admin
            if (recovery.ProvidedShares.Any(s => s.AdminSid == request.AdminSid))
                return BadRequest("Admin has already provided a share.");

            var share = new VaultRecoveryShare
            {
                RecoveryRequestId = recovery.Id,
                AdminSid = request.AdminSid,
                EncryptedShareBlob = request.EncryptedShareBlob
            };
            
            _db.VaultRecoveryShares.Add(share);
            await _db.SaveChangesAsync();

            return Ok();
        }

        [HttpGet("status/{adSid}")]
        public async Task<ActionResult<RecoveryStatusResponseDto>> GetRecoveryStatus(string adSid)
        {
            var profile = await _db.UserProfiles.FirstOrDefaultAsync(u => u.AdSid == adSid);
            if (profile == null) return NotFound("Profile not found.");

            var recovery = await _db.VaultRecoveryRequests
                .Include(r => r.ProvidedShares)
                .Where(r => r.AdSid == adSid && !r.IsCompleted)
                .OrderByDescending(r => r.RequestedAt)
                .FirstOrDefaultAsync();

            if (recovery == null) return NotFound("No active recovery request.");

            var response = new RecoveryStatusResponseDto
            {
                RecoveryRequestId = recovery.Id,
                AdSid = recovery.AdSid,
                EncryptedMasterKeyBackup = profile.EncryptedMasterKeyBackup,
                RequiredShares = recovery.RequiredShares,
                IsCompleted = recovery.ProvidedShares.Count >= recovery.RequiredShares,
                EncryptedShareBlobs = recovery.ProvidedShares.Select(s => s.EncryptedShareBlob).ToList()
            };

            // If the threshold is reached, we consider the recovery fetchable and complete it 
            // so admins can't submit more.
            if (response.IsCompleted)
            {
                recovery.IsCompleted = true;
                await _db.SaveChangesAsync();
            }

            return Ok(response);
        }
    }
}
