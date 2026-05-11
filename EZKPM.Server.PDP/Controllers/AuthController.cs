using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using EZKPM.Server.PDP.Data;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace EZKPM.Server.PDP.Controllers
{
    [ApiController]
    [Route("api/v1/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly EzkpmDbContext _db;

        public AuthController(EzkpmDbContext db)
        {
            _db = db;
        }

        // --- DTOs ---
        public class InviteRequestDto
        {
            public string HashedSid { get; set; }
            public string HashedUsername { get; set; }
        }

        public class InviteResponseDto
        {
            public string PairingCode { get; set; }
            public Guid LinkId { get; set; }
            public DateTime ExpiresAt { get; set; }
        }

        private string GetCallerHashedSid()
        {
            // Extract the HashedSid from the JWT claims
            return User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sid");
        }

        private async Task<bool> IsCallerAdmin()
        {
            var callerSid = GetCallerHashedSid();
            if (string.IsNullOrEmpty(callerSid)) return false;

            var profile = await _db.UserProfiles.FirstOrDefaultAsync(u => u.HashedSid == callerSid);
            return profile != null && profile.IsAdmin;
        }

        [HttpPost("invite")]
        // [Authorize] // TODO: Enable once Desktop Client sends JWT
        public async Task<IActionResult> InviteUser([FromBody] InviteRequestDto request)
        {
            // Temporarily bypassed for testing Phase 2 until Client JWT is ready
            // if (!await IsCallerAdmin()) return Forbid("Only administrators can invite new users.");

            if (string.IsNullOrWhiteSpace(request.HashedSid) || string.IsNullOrWhiteSpace(request.HashedUsername))
                return BadRequest("HashedSid and HashedUsername are required.");

            // Check if user already exists and has a public key
            var existingProfile = await _db.UserProfiles.FirstOrDefaultAsync(u => u.HashedSid == request.HashedSid);
            if (existingProfile != null && !string.IsNullOrEmpty(existingProfile.IdentityPublicKey))
                return Conflict("User is already fully registered with an Identity Key.");

            // Generate a secure 8-character Pairing Code
            var pairingCodeBytes = RandomNumberGenerator.GetBytes(6);
            var pairingCode = Convert.ToBase64String(pairingCodeBytes).Replace("+", "").Replace("/", "").Substring(0, 8).ToUpper();

            // Hash the pairing code for storage
            using var sha256 = SHA256.Create();
            var codeHashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(pairingCode));
            var codeHash = Convert.ToBase64String(codeHashBytes);

            var invitation = new PairingInvitation
            {
                Id = Guid.NewGuid(),
                HashedSid = request.HashedSid,
                HashedUsername = request.HashedUsername,
                PairingCodeHash = codeHash,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(7) // Link is valid for 7 days
            };

            _db.PairingInvitations.Add(invitation);
            await _db.SaveChangesAsync();

            return Ok(new InviteResponseDto
            {
                PairingCode = pairingCode,
                LinkId = invitation.Id,
            });
        }

        // Note: The VerifyLink endpoint was removed because it was redundant. 
        // The Desktop Client now natively uses the Pairing Code from the Deep Link and registers the device directly.

        public class RegisterDeviceRequestDto
        {
            public string PairingCode { get; set; }
            public string HashedSid { get; set; }
            public string IdentityPublicKey { get; set; }
        }

        [HttpPost("register-device")]
        public async Task<IActionResult> RegisterDevice([FromBody] RegisterDeviceRequestDto request)
        {
            if (string.IsNullOrWhiteSpace(request.PairingCode) || string.IsNullOrWhiteSpace(request.HashedSid) || string.IsNullOrWhiteSpace(request.IdentityPublicKey))
                return BadRequest("Missing required fields.");

            // Hash the pairing code provided by the client
            using var sha256 = SHA256.Create();
            var codeHashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(request.PairingCode));
            var codeHash = Convert.ToBase64String(codeHashBytes);

            // Find the invitation
            var invitation = await _db.PairingInvitations.FirstOrDefaultAsync(i => i.PairingCodeHash == codeHash);
            
            if (invitation == null)
                return NotFound("Invalid pairing code.");

            if (DateTime.UtcNow > invitation.ExpiresAt)
            {
                _db.PairingInvitations.Remove(invitation);
                await _db.SaveChangesAsync();
                return BadRequest("This pairing code has expired.");
            }

            // Verify that the client's HashedSid matches the one the Admin intended to invite
            if (request.HashedSid != invitation.HashedSid)
            {
                return BadRequest("This pairing code was not issued for your user account.");
            }

            // Success! Register the public key
            var profile = await _db.UserProfiles.FirstOrDefaultAsync(u => u.HashedSid == request.HashedSid);
            if (profile == null)
            {
                profile = new UserProfile { HashedSid = request.HashedSid };
                _db.UserProfiles.Add(profile);
            }

            profile.IdentityPublicKey = request.IdentityPublicKey;

            // Delete the one-time pairing code
            _db.PairingInvitations.Remove(invitation);
            
            await _db.SaveChangesAsync();

            return Ok(new { Status = "Success", Message = "Device successfully paired." });
        }
    }
}
