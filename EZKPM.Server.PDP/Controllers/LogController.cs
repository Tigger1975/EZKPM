using System;
using System.Linq;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using EZKPM.Server.PDP.Data;

namespace EZKPM.Server.PDP.Controllers
{
    [ApiController]
    [Route("api/v1/[controller]")]
    public class LogController : ControllerBase
    {
        private readonly EzkpmDbContext _db;

        public LogController(EzkpmDbContext db)
        {
            _db = db;
        }

        public class ClientLogDto
        {
            public string MachineName { get; set; }
            public string Username { get; set; }
            public string Level { get; set; }
            public string Message { get; set; }
            public DateTime Timestamp { get; set; }
        }

        [HttpPost("batch")]
        public async Task<IActionResult> SubmitBatch([FromBody] List<ClientLogDto> logs)
        {
            if (logs == null || logs.Count == 0) return BadRequest();

            foreach (var log in logs)
            {
                _db.ClientLogs.Add(new ClientLog
                {
                    MachineName = log.MachineName ?? "Unknown",
                    Username = log.Username ?? "Unknown",
                    Level = log.Level ?? "INFO",
                    Message = log.Message ?? "",
                    Timestamp = log.Timestamp
                });
            }

            await _db.SaveChangesAsync();
            return Ok(new { Status = "Success" });
        }

        [HttpGet("envkey")]
        public async Task<IActionResult> GetEnvKey()
        {
            var conf = await _db.GlobalConfigs.FindAsync("EnvironmentLogKey.PublicKey");
            if (conf == null) return NotFound();
            return Ok(new { PublicKey = conf.Value });
        }

        [HttpPost("envkey")]
        public async Task<IActionResult> SetEnvKey([FromBody] string publicKey)
        {
            var conf = await _db.GlobalConfigs.FindAsync("EnvironmentLogKey.PublicKey");
            if (conf == null)
            {
                _db.GlobalConfigs.Add(new GlobalConfig { Key = "EnvironmentLogKey.PublicKey", Value = publicKey });
            }
            else
            {
                conf.Value = publicKey;
            }
            await _db.SaveChangesAsync();
            return Ok();
        }

        [HttpGet("machines")]
        public async Task<IActionResult> GetMachines()
        {
            var machines = await _db.ClientLogs
                .Select(l => l.MachineName)
                .Distinct()
                .OrderBy(m => m)
                .ToListAsync();
            return Ok(machines);
        }

        [HttpGet("{machineName}")]
        public async Task<IActionResult> GetLogsForMachine(string machineName)
        {
            var logs = await _db.ClientLogs
                .Where(l => l.MachineName.ToLower() == machineName.ToLower())
                .OrderByDescending(l => l.Timestamp)
                .Take(100)
                .ToListAsync();

            var dtos = logs.Select(l => new ClientLogDto
            {
                MachineName = l.MachineName,
                Username = l.Username,
                Level = l.Level,
                Message = l.Message,
                Timestamp = l.Timestamp
            });

            return Ok(dtos);
        }

        private string GetUserPrimarySid()
        {
            string primarySid = User.FindFirstValue(ClaimTypes.PrimarySid) ?? User.FindFirstValue("sid");
            if (string.IsNullOrEmpty(primarySid))
            {
                try 
                {
                    if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                    {
                        var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                        primarySid = identity.User?.Value ?? "S-1-5-21-DUMMY-FALLBACK";
                    }
                }
                catch { /* Ignored */ }
            }
            if (string.IsNullOrEmpty(primarySid)) primarySid = Environment.UserName; // Linux fallback
            return EZKPM.Server.PDP.Services.SidHasher.HashSid(primarySid);
        }

        private async Task<bool> IsCallerAdminAsync()
        {
            var hashedPrimary = GetUserPrimarySid();
            var callerProfile = await _db.UserProfiles.FirstOrDefaultAsync(u => u.HashedSid == hashedPrimary);
            return callerProfile != null && await _db.UserProfiles.AnyAsync(u => u.PersonId == callerProfile.PersonId && u.IsAdmin);
        }

        [Authorize(AuthenticationSchemes = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme)]
        [HttpDelete]
        public async Task<IActionResult> DeleteAllLogs()
        {
            if (!await IsCallerAdminAsync()) return Forbid();

            _db.ClientLogs.RemoveRange(_db.ClientLogs);
            await _db.SaveChangesAsync();
            return Ok(new { Status = "Success" });
        }

        [Authorize(AuthenticationSchemes = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme)]
        [HttpDelete("envkey")]
        public async Task<IActionResult> DeleteEnvKey()
        {
            if (!await IsCallerAdminAsync()) return Forbid();

            var conf = await _db.GlobalConfigs.FindAsync("EnvironmentLogKey.PublicKey");
            if (conf != null)
            {
                _db.GlobalConfigs.Remove(conf);
                await _db.SaveChangesAsync();
            }
            return Ok(new { Status = "Success" });
        }
    }
}
