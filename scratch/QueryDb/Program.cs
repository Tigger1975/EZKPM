using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using EZKPM.Server.PDP.Data;

namespace QueryDb
{
    class Program
    {
        static void Main(string[] args)
        {
            var options = new DbContextOptionsBuilder<EzkpmDbContext>()
                .UseSqlite("Data Source=../../EZKPM.Server.PDP/pdp_vault.db")
                .Options;

            using (var db = new EzkpmDbContext(options))
            {
                var assetId = Guid.Parse("0006f18a-c592-41ff-9866-4ee3eda7090e");
                var asset = db.VaultAssets.Include(a => a.Acls).FirstOrDefault(a => a.Id == assetId);
                
                if (asset == null)
                {
                    Console.WriteLine("Asset not found. Creating test asset...");
                    asset = new VaultAsset { Id = assetId, CipherBlob = new byte[0], Nonce = new byte[0], MetadataHash = new byte[0], IsDeleted = true };
                    db.VaultAssets.Add(asset);
                    db.AssetAcls.Add(new AssetAcl { AssetId = assetId, HashedSid = "test", PermissionLevel = 3, EncryptedKeyShare = new byte[0] });
                    db.AuditLogs.Add(new AuditLog { AssetId = assetId, ActionType = "AssetCreated", EncryptedLogBlob = new byte[0], Nonce = new byte[0], PreviousEntryHash = new byte[0], CurrentEntryHash = new byte[0] });
                    db.SaveChanges();
                    Console.WriteLine("Asset created and soft-deleted.");
                }

                // Simulate hard delete
                if (asset.IsDeleted)
                {
                    var logs = db.AuditLogs.Where(l => l.AssetId == asset.Id).ToList();
                    foreach (var log in logs) log.AssetId = null;
                    
                    db.VaultAssets.Remove(asset);
                }

                try {
                    db.SaveChanges();
                    Console.WriteLine("Delete OK!");
                } catch (Exception ex) {
                    Console.WriteLine("Error: " + ex.Message);
                    if (ex.InnerException != null) Console.WriteLine("Inner: " + ex.InnerException.Message);
                }
            }
        }
    }
}
