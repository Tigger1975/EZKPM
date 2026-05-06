using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EZKPM.Server.PDP.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EZKPM.Server.PDP.Services
{
    public class GossipSyncEngine : BackgroundService
    {
        private readonly ILogger<GossipSyncEngine> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _config;
        private readonly P2PSyncTrigger _syncTrigger;
        private DateTime _lastSyncUtc = DateTime.MinValue;

        public GossipSyncEngine(ILogger<GossipSyncEngine> logger, IServiceProvider serviceProvider, IConfiguration config, P2PSyncTrigger syncTrigger)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _config = config;
            _syncTrigger = syncTrigger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Gossip Sync Engine started.");
            
            var p2pConfig = _config.GetSection("P2PConfig");
            var peers = p2pConfig.GetSection("Peers").Get<string[]>() ?? Array.Empty<string>();
            var p2pToken = p2pConfig.GetValue<string>("P2PToken");
            var nodeId = p2pConfig.GetValue<string>("NodeId");

            if (peers.Length == 0 || string.IsNullOrEmpty(p2pToken))
            {
                _logger.LogInformation("No P2P peers or token configured. Sync Engine idle.");
                return; // Nothing to do
            }

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", p2pToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<EzkpmDbContext>();

                    var newAssets = await db.VaultAssets
                        .Include(a => a.Acls)
                        .Where(a => a.UpdatedUtc > _lastSyncUtc)
                        .ToListAsync(stoppingToken);

                    var newLogs = await db.AuditLogs
                        .Where(l => l.Timestamp > _lastSyncUtc)
                        .ToListAsync(stoppingToken);

                    if (newAssets.Any() || newLogs.Any())
                    {
                        var payload = new
                        {
                            OriginNodeId = nodeId,
                            Assets = newAssets,
                            Logs = newLogs
                        };

                        string json = JsonSerializer.Serialize(payload);
                        var content = new StringContent(json, Encoding.UTF8, "application/json");

                        foreach (var peer in peers)
                        {
                            try
                            {
                                var response = await httpClient.PostAsync($"{peer.TrimEnd('/')}/api/p2p/push", content, stoppingToken);
                                if (response.IsSuccessStatusCode)
                                {
                                    _logger.LogInformation($"Successfully pushed {newAssets.Count} assets to {peer}");
                                }
                                else
                                {
                                    _logger.LogWarning($"Failed to push to {peer}: {response.StatusCode}");
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError($"Error syncing with peer {peer}: {ex.Message}");
                            }
                        }

                        if (newAssets.Any()) _lastSyncUtc = newAssets.Max(a => a.UpdatedUtc);
                        if (newLogs.Any()) 
                        {
                            var maxLog = newLogs.Max(l => l.Timestamp);
                            if (maxLog > _lastSyncUtc) _lastSyncUtc = maxLog;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Sync Engine Error: {ex.Message}");
                }

                // Wait for the next DB write event instead of polling
                // Or wake up every 5 minutes to perform a "Catch-up Pull" for offline nodes
                using var ctsTimeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                ctsTimeout.CancelAfter(TimeSpan.FromMinutes(5));

                try 
                {
                    await _syncTrigger.WaitAsync(ctsTimeout.Token);
                    await Task.Delay(500, stoppingToken); // Debounce for multiple rapid DB writes
                }
                catch (OperationCanceledException)
                {
                    if (stoppingToken.IsCancellationRequested) break;
                    // Timeout hit -> Perform Pull Catch-up!
                    await PerformPullCatchupAsync(peers, httpClient, stoppingToken);
                }
            }
        }

        private async Task PerformPullCatchupAsync(string[] peers, HttpClient httpClient, CancellationToken stoppingToken)
        {
            _logger.LogInformation("Performing periodic P2P Catch-up Pull...");
            
            DateTime latestLocalUtc = DateTime.MinValue;
            using (var scope = _serviceProvider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<EzkpmDbContext>();
                var maxAsset = await db.VaultAssets.MaxAsync(a => (DateTime?)a.UpdatedUtc, stoppingToken);
                var maxLog = await db.AuditLogs.MaxAsync(l => (DateTime?)l.Timestamp, stoppingToken);
                
                if (maxAsset.HasValue && maxAsset.Value > latestLocalUtc) latestLocalUtc = maxAsset.Value;
                if (maxLog.HasValue && maxLog.Value > latestLocalUtc) latestLocalUtc = maxLog.Value;
            }

            foreach (var peer in peers)
            {
                try
                {
                    var response = await httpClient.GetAsync($"{peer.TrimEnd('/')}/api/p2p/pull?since={latestLocalUtc:O}", stoppingToken);
                    if (response.IsSuccessStatusCode)
                    {
                        var jsonStr = await response.Content.ReadAsStringAsync();
                        var payload = JsonDocument.Parse(jsonStr).RootElement;
                        
                        var assetsCount = payload.GetProperty("Assets").GetArrayLength();
                        var logsCount = payload.GetProperty("Logs").GetArrayLength();
                        
                        if (assetsCount > 0 || logsCount > 0)
                        {
                            _logger.LogInformation($"Catch-up: Found {assetsCount} new assets and {logsCount} new logs from {peer}. Pushing to local controller...");
                            
                            // To keep it DRY, we just send this payload to our OWN Push endpoint locally,
                            // or we can invoke the DB logic directly. For simplicity in a P2P mesh, 
                            // posting it to our own Push endpoint executes the exact same LWW conflict resolution.
                            var localContent = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                            var localUrl = _config.GetValue<string>("P2PConfig:LocalUrl") ?? "https://localhost:5001/api/p2p/push";
                            await httpClient.PostAsync(localUrl, localContent, stoppingToken);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Catch-up pull failed for peer {peer}: {ex.Message}");
                }
            }
        }
    }
}
