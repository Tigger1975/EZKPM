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
                .UseSqlite("Data Source=C:\\inetpub\\EZKPM\\ezkpm_vault.db")
                .Options;

            using (var db = new EzkpmDbContext(options))
            {
                var profiles = db.UserProfiles.ToList();
                Console.WriteLine("--- User Profiles ---");
                foreach (var profile in profiles)
                {
                    Console.WriteLine($"SID: {profile.HashedSid}, HasKey: {!string.IsNullOrEmpty(profile.IdentityPublicKey)}");
                }
                
                var audits = db.AuditLogs.OrderByDescending(a => a.Timestamp).Take(5).ToList();
                Console.WriteLine("--- Audit Logs ---");
                foreach (var a in audits)
                {
                    Console.WriteLine($"[{a.Timestamp}] {a.ActionType} on {a.TargetHashedSid}");
                }
            }
        }
    }
}
