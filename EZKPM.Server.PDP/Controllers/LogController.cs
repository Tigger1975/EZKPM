using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
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
    }
}
