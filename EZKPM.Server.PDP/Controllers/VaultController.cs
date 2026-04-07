using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using EZKPM.Server.PDP.Data;

namespace EZKPM.Server.PDP.Controllers
{
    /// <summary>
    /// Zentraler API-Endpunkt (PDP) für den Abruf verschlüsselter Assets.
    /// Erzwingt Zero-Knowledge, AD-SID-Prüfungen und Expiration-Policies.
    /// </summary>
    [Authorize] // OIDC/JWT Auth zwingend erforderlich
    [ApiController]
    [Route("api/v1/vault")]
    public class VaultController : ControllerBase
    {
        private readonly EzkpmDbContext _db;

        public VaultController(EzkpmDbContext db)
        {
            _db = db;
        }

        [HttpGet("assets/{id}")]
        public async Task<IActionResult> GetAsset(Guid id)
        {
            // 1. Identität: AD SID aus dem Token extrahieren (ClaimType abhängig vom OIDC Provider)
            var userSid = User.FindFirstValue(ClaimTypes.PrimarySid) ?? User.FindFirstValue("sid");
            if (string.IsNullOrEmpty(userSid))
            {
                // Security-Event: Login ohne SID. Evtl. Token-Manipulation oder Fehlkonfiguration im AD.
                return Unauthorized(new { Error = "Keine AD-SID im Token gefunden. Zugriff verweigert." });
            }

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
            var responseDto = new
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
    }
}