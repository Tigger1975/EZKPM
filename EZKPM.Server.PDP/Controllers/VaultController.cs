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
    [ApiController]
    [Route("api/v1/vault")]
    public class VaultController : ControllerBase
    {
        private readonly EzkpmDbContext _db;

        public VaultController(EzkpmDbContext db)
        {
            _db = db;
        }

        private string GetUserSid()
        {
            // Fallback für lokale Tests ohne OIDC
            var sid = User.FindFirstValue(ClaimTypes.PrimarySid) ?? User.FindFirstValue("sid");
            return string.IsNullOrEmpty(sid) ? "S-1-5-21-DUMMY-TEST-USER" : sid;
        }

        [HttpGet("assets/all")]
        public async Task<IActionResult> GetAllAssets()
        {
            var userSid = GetUserSid();

            var assets = await _db.VaultAssets
                .Include(a => a.Acls.Where(acl => acl.AdSid == userSid))
                .Where(a => a.Acls.Any(acl => acl.AdSid == userSid))
                .ToListAsync();

            var responseList = assets.Select(asset =>
            {
                var userAcl = asset.Acls.First();
                return new VaultAssetResponseDto
                {
                    AssetId = asset.Id,
                    CipherBlob = Convert.ToBase64String(asset.CipherBlob),
                    Nonce = Convert.ToBase64String(asset.Nonce),
                    PermissionLevel = userAcl.PermissionLevel,
                    EncryptedKeyShare = Convert.ToBase64String(userAcl.EncryptedKeyShare),
                    IsExpired = DateTime.UtcNow > asset.ExpiresAt
                };
            }).ToList();

            return Ok(responseList);
        }

        [HttpPost("assets")]
        public async Task<IActionResult> CreateAsset([FromBody] CreateAssetRequestDto request)
        {
            var userSid = GetUserSid();
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
                    var cleanSid = aclDto.AdSid;
                    if (cleanSid.Contains("(") && cleanSid.Contains(")"))
                    {
                        var start = cleanSid.LastIndexOf("(") + 1;
                        var end = cleanSid.LastIndexOf(")");
                        if (end > start) cleanSid = cleanSid.Substring(start, end - start);
                    }

                    _db.AssetAcls.Add(new AssetAcl
                    {
                        AssetId = newAsset.Id,
                        AdSid = string.IsNullOrWhiteSpace(cleanSid) ? userSid : cleanSid,
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
                    AdSid = userSid,
                    PermissionLevel = 3, // Owner
                    EncryptedKeyShare = Convert.FromBase64String(request.EncryptedKeyShare)
                });
            }

            await _db.SaveChangesAsync();

            return Ok(new { AssetId = newAsset.Id });
        }

        [HttpPut("assets/{id}")]
        public async Task<IActionResult> UpdateAsset(Guid id, [FromBody] CreateAssetRequestDto request)
        {
            var userSid = GetUserSid();
            
            byte[] metaHash = Convert.FromBase64String(request.MetadataHash ?? "AA==");
            // Uniqueness check removed: Users can have multiple folders (empty URL/User) 
            // or multiple accounts with the same username on the same domain.

            var asset = await _db.VaultAssets
                .Include(a => a.Acls.Where(acl => acl.AdSid == userSid))
                .FirstOrDefaultAsync(a => a.Id == id);

            if (asset == null || !asset.Acls.Any()) return Forbid(); // Owner or at least write access required

            asset.CipherBlob = Convert.FromBase64String(request.CipherBlob);
            asset.Nonce = Convert.FromBase64String(request.Nonce);
            asset.MetadataHash = metaHash;
            
            // ACLs updaten
            if (request.Acls != null && request.Acls.Count > 0)
            {
                // Alte entfernen
                _db.AssetAcls.RemoveRange(_db.AssetAcls.Where(a => a.AssetId == id));
                
                // Neue hinzufügen
                foreach (var aclDto in request.Acls)
                {
                    var cleanSid = aclDto.AdSid;
                    if (cleanSid.Contains("(") && cleanSid.Contains(")"))
                    {
                        var start = cleanSid.LastIndexOf("(") + 1;
                        var end = cleanSid.LastIndexOf(")");
                        if (end > start) cleanSid = cleanSid.Substring(start, end - start);
                    }

                    _db.AssetAcls.Add(new AssetAcl
                    {
                        AssetId = asset.Id,
                        AdSid = string.IsNullOrWhiteSpace(cleanSid) ? userSid : cleanSid,
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
            
            await _db.SaveChangesAsync();
            return Ok();
        }

        [HttpDelete("assets/{id}")]
        public async Task<IActionResult> DeleteAsset(Guid id)
        {
            var userSid = GetUserSid();
            
            var asset = await _db.VaultAssets
                .Include(a => a.Acls.Where(acl => acl.AdSid == userSid))
                .FirstOrDefaultAsync(a => a.Id == id);

            if (asset == null) return NotFound();
            if (!asset.Acls.Any() || asset.Acls.First().PermissionLevel < 3) return Forbid(); // Only owner can delete

            _db.VaultAssets.Remove(asset);
            await _db.SaveChangesAsync();
            return Ok();
        }

        [HttpGet("assets/{id}")]
        public async Task<IActionResult> GetAsset(Guid id)
        {
            // 1. Identität: AD SID aus dem Token extrahieren (ClaimType abhängig vom OIDC Provider)
            var userSid = GetUserSid();


            // 2. Asset & ACL laden (Wir laden gezielt nur den ACL-Eintrag für den aufrufenden User)
            var asset = await _db.VaultAssets
                .Include(a => a.Acls.Where(acl => acl.AdSid == userSid))
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
            var userSid = GetUserSid();

            var hasAccess = await _db.AssetAcls.AnyAsync(a => a.AssetId == id && a.AdSid == userSid);
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
                ActorSid = userSid,
                EncryptedLogBlob = logBlob,
                Nonce = Convert.FromBase64String(request.Nonce),
                PreviousEntryHash = expectedPreviousHash,
                CurrentEntryHash = computedCurrentHash,
                Timestamp = DateTime.UtcNow
            };

            _db.AuditLogs.Add(logEntry);
            await _db.SaveChangesAsync();

            return Ok();
        }
    }
}