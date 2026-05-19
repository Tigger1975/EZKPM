using System;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using EZKPM.Server.PDP.Data;
using EZKPM.Shared.Contracts;

namespace EZKPM.Server.PDP.Controllers
{
    /// <summary>
    /// Zentraler API-Endpunkt (PDP) für den Abruf verschlüsselter Assets.
    /// Erzwingt Zero-Knowledge, AD-SID-Prüfungen und Expiration-Policies.
    /// </summary>
    [Authorize(AuthenticationSchemes = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme)]
    [ApiController]
    [Route("api/v1/vault")]
    public class VaultController : ControllerBase
    {
        private readonly EzkpmDbContext _db;
        private readonly Services.P2PSyncTrigger _syncTrigger;
        private readonly Microsoft.AspNetCore.SignalR.IHubContext<Hubs.ClientSyncHub> _clientSyncHub;

        public VaultController(EzkpmDbContext db, Services.P2PSyncTrigger syncTrigger, Microsoft.AspNetCore.SignalR.IHubContext<Hubs.ClientSyncHub> clientSyncHub)
        {
            _db = db;
            _syncTrigger = syncTrigger;
            _clientSyncHub = clientSyncHub;
        }

        private (string PrimarySid, List<string> AllSids) GetUserSids()
        {
            string primarySid = User.FindFirstValue(ClaimTypes.PrimarySid) ?? User.FindFirstValue("sid");
            var allSids = new List<string>();

            if (string.IsNullOrEmpty(primarySid))
            {
                try 
                {
                    if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                    {
                        var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                        primarySid = identity.User?.Value ?? "S-1-5-21-DUMMY-FALLBACK";
                        if (identity.Groups != null)
                        {
                            foreach (var group in identity.Groups)
                            {
                                allSids.Add(EZKPM.Server.PDP.Services.SidHasher.HashSid(group.Value));
                            }
                        }
                    }
                }
                catch { /* Ignored */ }
            }
            else
            {
                // We have claims from IIS
                var groupClaims = User.FindAll(ClaimTypes.GroupSid);
                foreach (var claim in groupClaims)
                {
                    allSids.Add(EZKPM.Server.PDP.Services.SidHasher.HashSid(claim.Value));
                }
            }

            if (string.IsNullOrEmpty(primarySid)) primarySid = Environment.UserName; // Linux fallback
            
            var hashedPrimary = EZKPM.Server.PDP.Services.SidHasher.HashSid(primarySid);
            allSids.Add(hashedPrimary);
            
            // Add dummy SID for tests
            allSids.Add(EZKPM.Server.PDP.Services.SidHasher.HashSid("S-1-5-21-DUMMY-TEST-USER"));

            return (hashedPrimary, allSids);
        }

        [HttpGet("assets/all")]
        public async Task<IActionResult> GetAllAssets()
        {
            var userSidsInfo = GetUserSids();
            var allSids = userSidsInfo.AllSids;
            var primarySid = userSidsInfo.PrimarySid;

            var callerProfile = await _db.UserProfiles.FirstOrDefaultAsync(u => u.HashedSid == primarySid);
            bool isAdmin = callerProfile != null && await _db.UserProfiles.AnyAsync(u => u.PersonId == callerProfile.PersonId && u.IsAdmin);

            List<VaultAsset> assets;
            if (isAdmin)
            {
                assets = await _db.VaultAssets
                    .Include(a => a.Acls)
                    .ToListAsync();
            }
            else
            {
                assets = await _db.VaultAssets
                    .Include(a => a.Acls.Where(acl => allSids.Contains(acl.HashedSid)))
                    .Where(a => a.Acls.Any(acl => allSids.Contains(acl.HashedSid)))
                    .ToListAsync();
            }

            var responseList = assets.Select(asset =>
            {
                var userAcl = asset.Acls.FirstOrDefault(acl => allSids.Contains(acl.HashedSid));
                bool isExpired = DateTime.UtcNow > asset.ExpiresAt;
                
                return new VaultAssetResponseDto
                {
                    AssetId = asset.Id,
                    CipherBlob = isExpired ? "" : Convert.ToBase64String(asset.CipherBlob),
                    Nonce = isExpired ? "" : Convert.ToBase64String(asset.Nonce),
                    PermissionLevel = userAcl?.PermissionLevel ?? 0,
                    EncryptedKeyShare = isExpired || userAcl == null || userAcl.EncryptedKeyShare == null || userAcl.EncryptedKeyShare.Length == 0 ? "" : Convert.ToBase64String(userAcl.EncryptedKeyShare),
                    IsExpired = isExpired,
                    IsDeleted = asset.IsDeleted
                };
            }).ToList();

            return Ok(responseList);
        }

        [HttpPost("assets")]
        public async Task<IActionResult> CreateAsset([FromBody] CreateAssetRequestDto request)
        {
            var userSidsInfo = GetUserSids();
            var userSid = userSidsInfo.PrimarySid;
            byte[] metaHash = Convert.FromBase64String(request.MetadataHash ?? "AA==");

            // Uniqueness check removed: Users can have multiple folders (empty URL/User) 
            // or multiple accounts with the same username on the same domain.

            var newAsset = new VaultAsset
            {
                Id = Guid.NewGuid(),
                MetadataHash = metaHash,
                CipherBlob = Convert.FromBase64String(request.CipherBlob),
                Nonce = Convert.FromBase64String(request.Nonce),
                ExpiresAt = request.ExpiresAt
            };

            _db.VaultAssets.Add(newAsset);
            
            if (request.Acls != null && request.Acls.Count > 0)
            {
                foreach (var aclDto in request.Acls)
                {
                    // Clean up potential SID string if it contains DisplayName
                    var cleanSid = aclDto.HashedSid;
                    if (cleanSid.Contains("(") && cleanSid.Contains(")"))
                    {
                        var start = cleanSid.LastIndexOf("(") + 1;
                        var end = cleanSid.LastIndexOf(")");
                        if (end > start) cleanSid = cleanSid.Substring(start, end - start);
                    }

                    _db.AssetAcls.Add(new AssetAcl
                    {
                        AssetId = newAsset.Id,
                        HashedSid = EZKPM.Server.PDP.Services.SidHasher.HashSid(string.IsNullOrWhiteSpace(cleanSid) ? userSid : cleanSid),
                        PermissionLevel = aclDto.PermissionLevel,
                        EncryptedKeyShare = string.IsNullOrWhiteSpace(aclDto.EncryptedKeyShare) 
                            ? Convert.FromBase64String(request.EncryptedKeyShare) 
                            : Convert.FromBase64String(aclDto.EncryptedKeyShare)
                    });
                }
            }
            else
            {
                _db.AssetAcls.Add(new AssetAcl
                {
                    AssetId = newAsset.Id,
                    HashedSid = userSid,
                    PermissionLevel = 3, // Owner
                    EncryptedKeyShare = Convert.FromBase64String(request.EncryptedKeyShare)
                });
            }

            await AppendAuditLog(newAsset.Id, userSid, "AssetCreated");
            await _db.SaveChangesAsync();
            _syncTrigger.Trigger();
            NotifyClients();

            return Ok(new { AssetId = newAsset.Id });
        }

        [HttpPut("assets/{id}")]
        public async Task<IActionResult> UpdateAsset(Guid id, [FromBody] CreateAssetRequestDto request)
        {
            var userSidsInfo = GetUserSids();
            var allSids = userSidsInfo.AllSids;
            var userSid = userSidsInfo.PrimarySid;
            var dummySid = EZKPM.Server.PDP.Services.SidHasher.HashSid("S-1-5-21-DUMMY-TEST-USER");
            
            byte[] metaHash = Convert.FromBase64String(request.MetadataHash ?? "AA==");

            var asset = await _db.VaultAssets
                .Include(a => a.Acls.Where(acl => allSids.Contains(acl.HashedSid)))
                .FirstOrDefaultAsync(a => a.Id == id);

            if (asset == null || !asset.Acls.Any()) return Forbid(); // Owner or at least write access required

            asset.CipherBlob = Convert.FromBase64String(request.CipherBlob);
            asset.Nonce = Convert.FromBase64String(request.Nonce);
            asset.MetadataHash = metaHash;
            asset.UpdatedUtc = DateTime.UtcNow;
            
            // ACLs updaten
            if (request.Acls != null && request.Acls.Count > 0)
            {
                // Alte entfernen
                _db.AssetAcls.RemoveRange(_db.AssetAcls.Where(a => a.AssetId == id));
                
                // Neue hinzufügen (deduplicated by SID)
                var uniqueAcls = request.Acls.GroupBy(a => 
                {
                    var clean = a.HashedSid;
                    if (clean.Contains("(") && clean.Contains(")"))
                    {
                        var start = clean.LastIndexOf("(") + 1;
                        var end = clean.LastIndexOf(")");
                        if (end > start) clean = clean.Substring(start, end - start);
                    }
                    return EZKPM.Server.PDP.Services.SidHasher.HashSid(string.IsNullOrWhiteSpace(clean) ? userSid : clean);
                }).Select(g => g.First());

                foreach (var aclDto in uniqueAcls)
                {
                    var cleanSid = aclDto.HashedSid;
                    if (cleanSid.Contains("(") && cleanSid.Contains(")"))
                    {
                        var start = cleanSid.LastIndexOf("(") + 1;
                        var end = cleanSid.LastIndexOf(")");
                        if (end > start) cleanSid = cleanSid.Substring(start, end - start);
                    }

                    _db.AssetAcls.Add(new AssetAcl
                    {
                        AssetId = asset.Id,
                        HashedSid = EZKPM.Server.PDP.Services.SidHasher.HashSid(string.IsNullOrWhiteSpace(cleanSid) ? userSid : cleanSid),
                        PermissionLevel = aclDto.PermissionLevel,
                        EncryptedKeyShare = string.IsNullOrWhiteSpace(aclDto.EncryptedKeyShare) 
                            ? Convert.FromBase64String(request.EncryptedKeyShare) 
                            : Convert.FromBase64String(aclDto.EncryptedKeyShare)
                    });
                }
            }
            else
            {
                // Fallback: Wenn keine Acls geschickt wurden, behalte zumindest den Owner-Key aktuell
                var acl = asset.Acls.First();
                acl.EncryptedKeyShare = Convert.FromBase64String(request.EncryptedKeyShare);
            }
            
            await AppendAuditLog(asset.Id, userSid, "AssetModified");
            await _db.SaveChangesAsync();
            _syncTrigger.Trigger();
            NotifyClients();
            return Ok();
        }

        [HttpDelete("assets/{id}")]
        public async Task<IActionResult> DeleteAsset(Guid id, [FromQuery] bool forceAdmin = false)
        {
            var userSidsInfo = GetUserSids();
            var allSids = userSidsInfo.AllSids;
            
            var asset = await _db.VaultAssets
                .Include(a => a.Acls.Where(acl => allSids.Contains(acl.HashedSid)))
                .FirstOrDefaultAsync(a => a.Id == id);

            if (asset == null) return NotFound();
            
            if (!forceAdmin) 
            {
                if (!asset.Acls.Any() || asset.Acls.First().PermissionLevel < 3) return Forbid(); // Only owner can delete
            }

            if (asset.IsDeleted)
            {
                var logs = await _db.AuditLogs.Where(l => l.AssetId == asset.Id).ToListAsync();
                foreach (var log in logs) log.AssetId = null;
                await _db.SaveChangesAsync(); // <-- Ensure logs are detached first
                
                _db.VaultAssets.Remove(asset);
                await _db.SaveChangesAsync();
            }
            else
            {
                asset.IsDeleted = true;
                asset.UpdatedUtc = DateTime.UtcNow;
                await AppendAuditLog(asset.Id, userSidsInfo.PrimarySid, "AssetDeleted");
                await _db.SaveChangesAsync();
            }

            _syncTrigger.Trigger();

            return Ok();
        }

        [HttpDelete("maintenance/orphans")]
        public async Task<IActionResult> CleanOrphanedAssets()
        {
            var userSidsInfo = GetUserSids();
            var allSids = userSidsInfo.AllSids;

            var allAssets = await _db.VaultAssets
                .Include(a => a.Acls)
                .ToListAsync();

            var orphanedAssets = allAssets.Where(a => 
            {
                if (!a.Acls.Any(acl => acl.PermissionLevel >= 3)) return true;

                bool hasAnyAccess = a.Acls.Any(acl => allSids.Contains(acl.HashedSid));
                if (!hasAnyAccess) return true;

                return false;
            }).ToList();

            if (orphanedAssets.Any())
            {
                var orphanedIds = orphanedAssets.Select(a => a.Id).ToList();
                var logs = await _db.AuditLogs.Where(l => l.AssetId.HasValue && orphanedIds.Contains(l.AssetId.Value)).ToListAsync();
                foreach (var log in logs) log.AssetId = null;
                await _db.SaveChangesAsync(); // <-- Ensure logs are detached first

                _db.VaultAssets.RemoveRange(orphanedAssets);
                await _db.SaveChangesAsync();
            }

            return Ok(new { DeletedCount = orphanedAssets.Count });
        }

        [HttpPut("assets/{id}/restore")]
        public async Task<IActionResult> RestoreAsset(Guid id)
        {
            var userSidsInfo = GetUserSids();
            var allSids = userSidsInfo.AllSids;

            var asset = await _db.VaultAssets
                .Include(a => a.Acls.Where(acl => allSids.Contains(acl.HashedSid)))
                .FirstOrDefaultAsync(a => a.Id == id);

            if (asset == null || !asset.Acls.Any()) return Forbid();

            asset.IsDeleted = false;
            asset.UpdatedUtc = DateTime.UtcNow;

            await AppendAuditLog(asset.Id, userSidsInfo.PrimarySid, "AssetRestored");
            await _db.SaveChangesAsync();
            _syncTrigger.Trigger();

            return Ok();
        }

        [HttpGet("assets/{id}")]
        public async Task<IActionResult> GetAsset(Guid id)
        {
            // 1. Identität: AD SID aus dem Token extrahieren (ClaimType abhängig vom OIDC Provider)
            var userSidsInfo = GetUserSids();
            var allSids = userSidsInfo.AllSids;

            // 2. Asset & ACL laden (Wir laden gezielt nur den ACL-Eintrag für den aufrufenden User)
            var asset = await _db.VaultAssets
                .Include(a => a.Acls.Where(acl => allSids.Contains(acl.HashedSid)))
                .FirstOrDefaultAsync(a => a.Id == id);

            if (asset == null)
            {
                // Um Metadaten-Leaking zu vermeiden, geben wir NotFound zurück (oder Forbid, beides verschleiert die Existenz vor Unbefugten)
                return NotFound();
            }

            var userAcl = asset.Acls.FirstOrDefault();
            if (userAcl == null)
            {
                return Forbid(); // Existiert, aber User hat keine Berechtigung
            }

            // 3. Rotation-Proof (Pflichtenheft FA 30)
            if (DateTime.UtcNow > asset.ExpiresAt)
            {
                // Level 3 = Owner. Nur der Owner darf abgelaufene Assets abrufen (für den Rotation Assistant).
                if (userAcl.PermissionLevel < 3)
                {
                    // HTTP 423 Locked - signalisiert dem Client, dass das Asset wegen Ablauf gesperrt ist.
                    return StatusCode(423, new
                    {
                        Error = "Asset-Lebenszeit abgelaufen (FA 30). Zugriff geblockt. Bitte kontaktieren Sie den Asset-Owner für eine Rotation."
                    });
                }
            }

            // 4. Zero-Knowledge-Auslieferung: Der Server gibt nur Chiffren und Nonces aus.
            var responseDto = new VaultAssetResponseDto
            {
                AssetId = asset.Id,
                CipherBlob = Convert.ToBase64String(asset.CipherBlob),
                Nonce = Convert.ToBase64String(asset.Nonce),
                PermissionLevel = userAcl.PermissionLevel,
                // Der mit dem Public Key des Users gewrappte Asset-Key (wird lokal auf dem PEP entschlüsselt)
                EncryptedKeyShare = Convert.ToBase64String(userAcl.EncryptedKeyShare),
                IsExpired = DateTime.UtcNow > asset.ExpiresAt // Flag für den Client UI/Rotation-Assistant
            };

            return Ok(responseDto);
        }

        /// <summary>
        /// Nimmt Audit-Logs entgegen (Pflicht für Payment-Assets, FA 22) und validiert die Hash-Chain (FA 4.2).
        /// </summary>
        [HttpPost("assets/{id}/audit")]
        public async Task<IActionResult> AppendAuditLog(Guid id, [FromBody] AuditLogRequestDto request)
        {
            var userSidsInfo = GetUserSids();
            var allSids = userSidsInfo.AllSids;
            var userSid = userSidsInfo.PrimarySid;
            var dummySid = EZKPM.Server.PDP.Services.SidHasher.HashSid("S-1-5-21-DUMMY-TEST-USER");

            var hasAccess = await _db.AssetAcls.AnyAsync(a => a.AssetId == id && allSids.Contains(a.HashedSid));
            if (!hasAccess) return Forbid();

            // 1. Letzten Log-Eintrag für dieses Asset holen, um die Kette zu prüfen
            var latestLog = await _db.AuditLogs
                .Where(l => l.AssetId == id)
                .OrderByDescending(l => l.Timestamp)
                .FirstOrDefaultAsync();

            // Genesis-Block Fallback: Wenn noch kein Log existiert, erwarten wir ein leeres 32-Byte Array
            byte[] expectedPreviousHash = latestLog != null ? latestLog.CurrentEntryHash : new byte[32];
            byte[] providedPrevHash = Convert.FromBase64String(request.PreviousEntryHash);

            if (!expectedPreviousHash.SequenceEqual(providedPrevHash))
            {
                // Security-Event: Jemand versucht die Chronologie zu fälschen oder ein Log-Eintrag fehlt
                return BadRequest(new { Error = "Hash-Chain validation failed. Previous hash mismatch (FA 4.2)." });
            }

            // 2. Hash-Verifikation: CurrentEntryHash = SHA256(PreviousEntryHash + EncryptedLogBlob)
            // Der Server prüft die Mathematik, auch wenn er den Klartext des Blobs nicht kennt.
            byte[] logBlob = Convert.FromBase64String(request.EncryptedLogBlob);
            byte[] providedCurrentHash = Convert.FromBase64String(request.CurrentEntryHash);

            using var sha256 = SHA256.Create();
            byte[] buffer = new byte[expectedPreviousHash.Length + logBlob.Length];
            Buffer.BlockCopy(expectedPreviousHash, 0, buffer, 0, expectedPreviousHash.Length);
            Buffer.BlockCopy(logBlob, 0, buffer, expectedPreviousHash.Length, logBlob.Length);

            byte[] computedCurrentHash = sha256.ComputeHash(buffer);

            if (!computedCurrentHash.SequenceEqual(providedCurrentHash))
            {
                return BadRequest(new { Error = "Hash-Chain validation failed. Current hash is mathematically invalid." });
            }

            // 3. Chain-Eintrag persistieren
            var logEntry = new AuditLog
            {
                AssetId = id,
                ActorHashedSid = userSid,
                EncryptedLogBlob = logBlob,
                Nonce = Convert.FromBase64String(request.Nonce),
                PreviousEntryHash = expectedPreviousHash,
                CurrentEntryHash = computedCurrentHash,
                Timestamp = DateTime.UtcNow
            };

            _db.AuditLogs.Add(logEntry);
            await _db.SaveChangesAsync();
            _syncTrigger.Trigger();

            return Ok();
        }

        [HttpGet("assets/{id}/audit/latest-hash")]
        public async Task<IActionResult> GetLatestAuditHash(Guid id)
        {
            var userSidsInfo = GetUserSids();
            var allSids = userSidsInfo.AllSids;

            var hasAccess = await _db.AssetAcls.AnyAsync(a => a.AssetId == id && allSids.Contains(a.HashedSid));
            if (!hasAccess) return Forbid();

            var latestLog = await _db.AuditLogs
                .Where(l => l.AssetId == id)
                .OrderByDescending(l => l.Timestamp)
                .FirstOrDefaultAsync();

            byte[] expectedHash = latestLog != null ? latestLog.CurrentEntryHash : new byte[32];
            return Ok(new { Hash = Convert.ToBase64String(expectedHash) });
        }
        private async void NotifyClients()
        {
            await Microsoft.AspNetCore.SignalR.ClientProxyExtensions.SendAsync(_clientSyncHub.Clients.All, "VaultUpdated");
        }

        private async Task AppendAuditLog(Guid assetId, string actorHashedSid, string actionType)
        {
            var prevLog = await _db.AuditLogs.Where(l => l.AssetId == assetId).OrderByDescending(l => l.Timestamp).FirstOrDefaultAsync();
            byte[] prevHash = prevLog?.CurrentEntryHash ?? new byte[32];
            byte[] currentHash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(assetId.ToString() + actionType + Convert.ToBase64String(prevHash)));

            _db.AuditLogs.Add(new AuditLog
            {
                AssetId = assetId,
                ActionType = actionType,
                ActorHashedSid = actorHashedSid,
                PreviousEntryHash = prevHash,
                CurrentEntryHash = currentHash,
                Timestamp = DateTime.UtcNow
            });
        }
    }
}
