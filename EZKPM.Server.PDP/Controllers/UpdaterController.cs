using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using EZKPM.Shared.Contracts;

namespace EZKPM.Server.PDP.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UpdaterController : ControllerBase
    {
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<UpdaterController> _logger;
        private readonly string _updatesDir;

        public UpdaterController(IWebHostEnvironment env, ILogger<UpdaterController> logger)
        {
            _env = env;
            _logger = logger;
            _updatesDir = Path.Combine(_env.ContentRootPath, "Updates");
            
            if (!Directory.Exists(_updatesDir))
            {
                Directory.CreateDirectory(_updatesDir);
            }
        }

        [HttpGet("check")]
        public IActionResult CheckForUpdate([FromQuery] string currentVersion)
        {
            try
            {
                var versionFile = Path.Combine(_updatesDir, "version.json");
                if (!System.IO.File.Exists(versionFile))
                {
                    return Ok(new UpdateCheckResponseDto { UpdateAvailable = false });
                }

                var json = System.IO.File.ReadAllText(versionFile);
                var info = System.Text.Json.JsonSerializer.Deserialize<UpdateCheckResponseDto>(json);
                
                if (info == null)
                    return Ok(new UpdateCheckResponseDto { UpdateAvailable = false });

                // Simple version comparison
                if (Version.TryParse(currentVersion, out var current) && Version.TryParse(info.LatestVersion, out var latest))
                {
                    info.UpdateAvailable = latest > current;
                }
                else
                {
                    // Fallback if parsing fails
                    info.UpdateAvailable = currentVersion != info.LatestVersion;
                }

                // Make sure the download URL is absolute or relative so the client can find it
                info.DownloadUrl = "/api/updater/download";
                
                return Ok(info);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check for updates.");
                return StatusCode(500, "Internal server error while checking for updates.");
            }
        }

        [HttpGet("download")]
        public IActionResult DownloadUpdate()
        {
            try
            {
                var updateFile = Path.Combine(_updatesDir, "ClientUpdate.zip");
                if (!System.IO.File.Exists(updateFile))
                {
                    return NotFound("No update file available on server.");
                }

                var stream = new FileStream(updateFile, FileMode.Open, FileAccess.Read, FileShare.Read);
                return File(stream, "application/zip", "EZKPM_Client_Update.zip");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to download update.");
                return StatusCode(500, "Internal server error while downloading update.");
            }
        }
    }
}
