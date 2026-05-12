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
            var chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ23456789";
            var stringChars = new char[8];
            var randomBytes = RandomNumberGenerator.GetBytes(8);
            for (int i = 0; i < stringChars.Length; i++)
            {
                stringChars[i] = chars[randomBytes[i] % chars.Length];
            }
            var pairingCode = new string(stringChars);

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

        [HttpGet("users")]
        public async Task<IActionResult> GetAllUsers()
        {
            // Temporarily bypassed for testing
            // if (!await IsCallerAdmin()) return Forbid("Only administrators can view users.");

            var users = await _db.UserProfiles
                .Select(u => new
                {
                    HashedSid = u.HashedSid,
                    IsAdmin = u.IsAdmin,
                    IsPaired = !string.IsNullOrEmpty(u.IdentityPublicKey),
                    LastLoginAt = u.LastLoginAt
                })
                .ToListAsync();

            var invites = await _db.PairingInvitations
                .Select(i => new
                {
                    HashedSid = i.HashedSid,
                    ExpiresAt = i.ExpiresAt
                })
                .ToListAsync();

            return Ok(new { RegisteredUsers = users, PendingInvites = invites });
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

            bool isGenesis = false;
            if (request.PairingCode == "GENESIS")
            {
                if (!await _db.UserProfiles.AnyAsync())
                {
                    isGenesis = true;
                }
                else
                {
                    return BadRequest("Genesis mode is only available for the very first user (database is not empty).");
                }
            }

            if (!isGenesis)
            {
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

                // Delete the one-time pairing code
                _db.PairingInvitations.Remove(invitation);
            }

            // Success! Register the public key
            var profile = await _db.UserProfiles.FirstOrDefaultAsync(u => u.HashedSid == request.HashedSid);
            bool isNew = false;
            if (profile == null)
            {
                profile = new UserProfile { HashedSid = request.HashedSid };
                if (isGenesis) profile.IsAdmin = true;
                _db.UserProfiles.Add(profile);
                isNew = true;
            }

            profile.IdentityPublicKey = request.IdentityPublicKey;

            // Generate AuditLog
            var prevLog = await _db.AuditLogs.Where(l => l.TargetHashedSid == request.HashedSid).OrderByDescending(l => l.Timestamp).FirstOrDefaultAsync();
            byte[] prevHash = prevLog?.CurrentEntryHash ?? new byte[32];
            byte[] currentHash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(request.HashedSid + (isNew ? "Created" : "Modified") + Convert.ToBase64String(prevHash)));

            _db.AuditLogs.Add(new AuditLog
            {
                ActionType = isNew ? "UserProfileCreated" : "UserProfileModified",
                TargetHashedSid = request.HashedSid,
                ActorHashedSid = request.HashedSid,
                PreviousEntryHash = prevHash,
                CurrentEntryHash = currentHash,
                Timestamp = DateTime.UtcNow
            });
            
            await _db.SaveChangesAsync();

            return Ok(new { Status = "Success", Message = "Device successfully paired." });
        }
        public class LoginRequestDto
        {
            public string HashedSid { get; set; }
            public long Timestamp { get; set; }
            public string Signature { get; set; } // Base64 ECDSA Signature
            public List<string> HashedGroupSids { get; set; }
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequestDto request, [FromServices] Microsoft.Extensions.Configuration.IConfiguration config)
        {
            var debugLog = "C:\\inetpub\\EZKPM\\login_debug.txt";
            try { System.IO.File.AppendAllText(debugLog, $"[{DateTime.UtcNow}] Login attempt for {request.HashedSid}\n"); } catch {}

            if (string.IsNullOrWhiteSpace(request.HashedSid) || string.IsNullOrWhiteSpace(request.Signature))
            {
                try { System.IO.File.AppendAllText(debugLog, $"[{DateTime.UtcNow}] Missing fields\n"); } catch {}
                return BadRequest("Missing required fields.");
            }

            // Check timestamp to prevent replay attacks (allow 5 mins drift)
            var requestTime = DateTimeOffset.FromUnixTimeSeconds(request.Timestamp);
            if (Math.Abs((DateTimeOffset.UtcNow - requestTime).TotalMinutes) > 5)
            {
                try { System.IO.File.AppendAllText(debugLog, $"[{DateTime.UtcNow}] Timestamp expired: requestTime={requestTime}, utcNow={DateTimeOffset.UtcNow}\n"); } catch {}
                return Unauthorized("Timestamp is invalid or expired.");
            }

            var profile = await _db.UserProfiles.FirstOrDefaultAsync(u => u.HashedSid == request.HashedSid);
            if (profile == null || string.IsNullOrEmpty(profile.IdentityPublicKey))
            {
                try { System.IO.File.AppendAllText(debugLog, $"[{DateTime.UtcNow}] Profile not found or missing key\n"); } catch {}
                return Unauthorized("User not registered or missing identity key.");
            }

            // Verify signature
            try
            {
                byte[] pubKeyBytes = Convert.FromBase64String(profile.IdentityPublicKey);
                using var ecdsa = ECDsa.Create();
                ecdsa.ImportSubjectPublicKeyInfo(pubKeyBytes, out _);

                string dataToSign = $"{request.HashedSid}:{request.Timestamp}";
                byte[] dataBytes = System.Text.Encoding.UTF8.GetBytes(dataToSign);
                byte[] signatureBytes = Convert.FromBase64String(request.Signature);

                if (!ecdsa.VerifyData(dataBytes, signatureBytes, HashAlgorithmName.SHA256))
                {
                    try { System.IO.File.AppendAllText(debugLog, $"[{DateTime.UtcNow}] Invalid signature\n"); } catch {}
                    return Unauthorized("Invalid signature.");
                }
            }
            catch (Exception ex)
            {
                try { System.IO.File.AppendAllText(debugLog, $"[{DateTime.UtcNow}] Signature exception: {ex.Message}\n"); } catch {}
                return Unauthorized("Signature validation failed.");
            }

            // Generate JWT
            var tokenHandler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
            var key = System.Text.Encoding.UTF8.GetBytes(config["Jwt:Key"] ?? "EZKPM_Fallback_Secret_Key_32_Bytes_Long_Minimum");
            var claims = new List<Claim> { new Claim(ClaimTypes.NameIdentifier, request.HashedSid), new Claim(ClaimTypes.PrimarySid, request.HashedSid) };
            if (request.HashedGroupSids != null)
            {
                foreach (var gSid in request.HashedGroupSids)
                {
                    claims.Add(new Claim(ClaimTypes.GroupSid, gSid));
                }
            }
            var tokenDescriptor = new Microsoft.IdentityModel.Tokens.SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddDays(1),
                Issuer = "EZKPM_Server",
                Audience = "EZKPM_Client",
                SigningCredentials = new Microsoft.IdentityModel.Tokens.SigningCredentials(new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(key), Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256Signature)
            };
            var token = tokenHandler.CreateToken(tokenDescriptor);
            var jwt = tokenHandler.WriteToken(token);

            profile.LastLoginAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return Ok(new { Token = jwt });
        }
    }
}
