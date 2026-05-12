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
                var logs = db.ClientLogs.OrderByDescending(l => l.Timestamp).Take(30).ToList();
                Console.WriteLine("--- Client Logs ---");
                foreach (var log in logs)
                {
                    Console.WriteLine($"[{log.Timestamp}] {log.Level} - {log.Message}");
                }
            }
        }
    }
}
