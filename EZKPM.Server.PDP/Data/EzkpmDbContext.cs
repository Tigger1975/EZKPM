using System;
using Microsoft.EntityFrameworkCore;

namespace EZKPM.Server.PDP.Data
{
    /// <summary>
    /// Zentraler Policy Decision Point (PDP) Database Context.
    /// WARNUNG: Dieser Context darf niemals Klartext-Datenbankfelder für Secrets definieren.
    /// Zero-Knowledge-Prinzip: Alles außer IDs und AD-SIDs MUSS verschlüsselt oder gehasht sein.
    /// </summary>
    public class EzkpmDbContext : DbContext
    {
        public DbSet<VaultAsset> VaultAssets { get; set; }
        public DbSet<AssetAcl> AssetAcls { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }
        public DbSet<UserProfile> UserProfiles { get; set; }
        public DbSet<VaultRecoveryRequest> VaultRecoveryRequests { get; set; }
        public DbSet<VaultRecoveryShare> VaultRecoveryShares { get; set; }
        public DbSet<RecoveryAuditLog> RecoveryAuditLogs { get; set; }

        public EzkpmDbContext(DbContextOptions<EzkpmDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            
            // Register OpenIddict models
            modelBuilder.UseOpenIddict();

            // --- VaultAsset Configuration ---
            modelBuilder.Entity<VaultAsset>(entity =>
            {
                entity.HasKey(e => e.Id);
                // Indizierung für deterministische Suchen (z.B. URL-Hash für Autofill)
                entity.HasIndex(e => e.MetadataHash);

                entity.Property(e => e.CipherBlob).IsRequired();
                entity.Property(e => e.Nonce).IsRequired().HasMaxLength(12); // GCM Nonce (96-bit)
            });

            // --- AssetAcl Configuration (AD SID Mapping) ---
            modelBuilder.Entity<AssetAcl>(entity =>
            {
                // Ein User/Gruppe (SID) hat genau eine Berechtigung pro Asset
                entity.HasKey(e => new { e.AssetId, e.AdSid });

                entity.HasOne(e => e.Asset)
                      .WithMany(a => a.Acls)
                      .HasForeignKey(e => e.AssetId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // --- AuditLog Configuration (Hash-Chaining) ---
            modelBuilder.Entity<AuditLog>(entity =>
            {
                entity.HasKey(e => e.Id);
                // Verkettung erzwingen durch Unique-Index auf PreviousEntryHash
                entity.HasIndex(e => e.PreviousEntryHash).IsUnique();

                entity.HasOne(e => e.Asset)
                      .WithMany()
                      .HasForeignKey(e => e.AssetId)
                      .OnDelete(DeleteBehavior.Restrict); // Logs dürfen NICHT gelöscht werden
            });

            // --- Recovery Configuration ---
            modelBuilder.Entity<UserProfile>(entity =>
            {
                entity.HasKey(e => e.AdSid);
            });

            modelBuilder.Entity<VaultRecoveryRequest>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasMany(e => e.ProvidedShares)
                      .WithOne()
                      .HasForeignKey(s => s.RecoveryRequestId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<VaultRecoveryShare>(entity =>
            {
                entity.HasKey(e => e.Id);
            });

            modelBuilder.Entity<RecoveryAuditLog>(entity =>
            {
                entity.HasKey(e => e.Id);
            });
        }
    }

    /// <summary>
    /// Repräsentiert ein verschlüsseltes Asset (Passwort, Passkey, Payment).
    /// </summary>
    public class VaultAsset
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// SHA-256 Hash der Metadaten (z.B. der URL). 
        /// Erlaubt dem Client, gezielt Einträge für eine Domain abzufragen, ohne die Domain dem Server zu verraten.
        /// </summary>
        public byte[] MetadataHash { get; set; }

        /// <summary>
        /// Der mit AES-256-GCM verschlüsselte Payload (Klartext-Metadaten, Username, Passwort).
        /// </summary>
        public byte[] CipherBlob { get; set; }

        /// <summary>
        /// Die 96-bit Nonce, die für die AES-GCM Verschlüsselung verwendet wurde.
        /// </summary>
        public byte[] Nonce { get; set; }

        /// <summary>
        /// Ablaufdatum gemäß Pflichtenheft FA 30 (Rotation-Proof). 
        /// Der Server weigert sich, den Blob nach Ablauf auszuliefern.
        /// </summary>
        public DateTime ExpiresAt { get; set; }

        public bool IsDeleted { get; set; } // Soft-Delete flag (Papierkorb)
        
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        public ICollection<AssetAcl> Acls { get; set; }
    }

    /// <summary>
    /// Access Control List: Verknüpft Active Directory SIDs mit Assets.
    /// </summary>
    public class AssetAcl
    {
        public Guid AssetId { get; set; }
        public VaultAsset Asset { get; set; }

        /// <summary>
        /// Security Identifier (SID) aus dem Active Directory (User oder Gruppe).
        /// </summary>
        public string AdSid { get; set; }

        /// <summary>
        /// Berechtigungsstufe: Execute (1), Read (2), Owner (3).
        /// </summary>
        public int PermissionLevel { get; set; }

        /// <summary>
        /// Der Asset-Key, asymmetrisch verschlüsselt mit dem Public Key des AD-Users/der AD-Gruppe (PQC/X25519).
        /// </summary>
        public byte[] EncryptedKeyShare { get; set; }
    }

    /// <summary>
    /// Revisionssicheres, kryptografisch verkettetes Audit-Log (Pflichtenheft FA 22 & 4.2).
    /// </summary>
    public class AuditLog
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid AssetId { get; set; }
        public VaultAsset Asset { get; set; }

        /// <summary>
        /// AD SID des Akteurs, der die Aktion ausgeführt hat.
        /// </summary>
        public string ActorSid { get; set; }

        /// <summary>
        /// Verschlüsselte Log-Details (z.B. Betrag, Bestellnummer bei Payments).
        /// </summary>
        public byte[] EncryptedLogBlob { get; set; }
        public byte[] Nonce { get; set; }

        /// <summary>
        /// Hash des chronologisch vorherigen Log-Eintrags für dieses Asset (Chaining).
        /// </summary>
        public byte[] PreviousEntryHash { get; set; }

        /// <summary>
        /// Hash des aktuellen Eintrags (inkl. EncryptedLogBlob + PreviousEntryHash).
        /// </summary>
        public byte[] CurrentEntryHash { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public class UserProfile
    {
        public string AdSid { get; set; }
        public string EncryptedMasterKeyBackup { get; set; }
        public bool IsAdmin { get; set; }
    }

    public class VaultRecoveryRequest
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string AdSid { get; set; } 
        public string RequesterSid { get; set; } // Enforces 6-eyes principle
        public string EphemeralUserPubKey { get; set; } 
        public int RequiredShares { get; set; } = 2; // e.g. 2-of-5 threshold
        public bool IsCompleted { get; set; }
        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

        public ICollection<VaultRecoveryShare> ProvidedShares { get; set; } = new List<VaultRecoveryShare>();
    }

    public class VaultRecoveryShare
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid RecoveryRequestId { get; set; }
        public string AdminSid { get; set; }
        public string EncryptedShareBlob { get; set; }
    }

    public class RecoveryAuditLog
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid RecoveryRequestId { get; set; }
        public string Action { get; set; } // e.g. "Requested", "Approved", "Completed", "Expired"
        public string ActorSid { get; set; } // Who did it
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string Details { get; set; } 
    }
}