using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using EZKPM.Server.PDP.Data;
using EZKPM.Shared.Contracts;
using Microsoft.AspNetCore.Authorization;

namespace EZKPM.Server.PDP.Controllers
{
    [ApiController]
    [Route("api/v1/security-alerts")]
    public class SecurityAlertsController : ControllerBase
    {
        private readonly EzkpmDbContext _db;

        public SecurityAlertsController(EzkpmDbContext db)
        {
            _db = db;
        }

        private string GetUserSid()
        {
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
#pragma warning disable CA1416
                var identity = HttpContext.User.Identity as System.Security.Principal.WindowsIdentity;
                return EZKPM.Server.PDP.Services.SidHasher.HashSid(identity?.User?.Value ?? "SYSTEM");
#pragma warning restore CA1416
            }
            return EZKPM.Server.PDP.Services.SidHasher.HashSid("SYSTEM");
        }

        private async Task<bool> IsAdminAsync()
        {
            var hashedSid = GetUserSid();
            var profile = await _db.UserProfiles.FirstOrDefaultAsync(u => u.HashedSid == hashedSid);
            return profile != null && await _db.UserProfiles.AnyAsync(u => u.PersonId == profile.PersonId && u.IsAdmin);
        }

        [HttpGet]
        public async Task<IActionResult> GetAlerts()
        {
            if (!await IsAdminAsync()) return Forbid();

            var alerts = await _db.SecurityAlerts
                .OrderByDescending(a => a.CreatedAt)
                .Select(a => new SecurityAlertResponseDto
                {
                    Id = a.Id,
                    PackageName = a.PackageName,
                    Version = a.Version,
                    Details = a.Details,
                    IsResolved = a.IsResolved,
                    CreatedAt = a.CreatedAt,
                    ResolvedAt = a.ResolvedAt,
                    ResolvedBySid = a.ResolvedBySid
                })
                .ToListAsync();

            return Ok(alerts);
        }

        [HttpPost]
        public async Task<IActionResult> ReportAlert([FromBody] ReportSecurityAlertDto request)
        {
            if (!await IsAdminAsync()) return Forbid();

            // Check if there is already an unresolved alert for this package and version
            var existing = await _db.SecurityAlerts.FirstOrDefaultAsync(a => 
                a.PackageName == request.PackageName && 
                a.Version == request.Version && 
                !a.IsResolved);

            if (existing == null)
            {
                var alert = new SecurityAlert
                {
                    PackageName = request.PackageName,
                    Version = request.Version,
                    Details = request.Details,
                    IsResolved = false,
                    CreatedAt = DateTime.UtcNow
                };
                _db.SecurityAlerts.Add(alert);
                await _db.SaveChangesAsync();
            }

            return Ok();
        }

        [HttpPost("{id}/resolve")]
        public async Task<IActionResult> ResolveAlert(Guid id)
        {
            if (!await IsAdminAsync()) return Forbid();

            var alert = await _db.SecurityAlerts.FindAsync(id);
            if (alert == null) return NotFound();

            if (!alert.IsResolved)
            {
                alert.IsResolved = true;
                alert.ResolvedAt = DateTime.UtcNow;
                alert.ResolvedBySid = GetUserSid();
                await _db.SaveChangesAsync();
            }

            return Ok();
        }
    }
}

