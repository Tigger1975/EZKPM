using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using EZKPM.Server.PDP.Data;

namespace EZKPM.Server.PDP.Services
{
    public class LogRotationService : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<LogRotationService> _logger;

        public LogRotationService(IServiceProvider services, ILogger<LogRotationService> logger)
        {
            _services = services;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using (var scope = _services.CreateScope())
                    {
                        var db = scope.ServiceProvider.GetRequiredService<EzkpmDbContext>();
                        var threshold = DateTime.UtcNow.AddDays(-30);
                        
                        var oldLogs = db.ClientLogs.Where(l => l.Timestamp < threshold).ToList();
                        if (oldLogs.Count > 0)
                        {
                            db.ClientLogs.RemoveRange(oldLogs);
                            await db.SaveChangesAsync(stoppingToken);
                            _logger.LogInformation($"LogRotationService: Removed {oldLogs.Count} old client logs.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error rotating logs");
                }

                await Task.Delay(TimeSpan.FromHours(12), stoppingToken);
            }
        }
    }
}
