using System;
using System.ComponentModel.DataAnnotations;

namespace EZKPM.Server.PDP.Data
{
    public class SecurityAlert
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();
        public string PackageName { get; set; }
        public string Version { get; set; }
        public string Details { get; set; }
        public bool IsResolved { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ResolvedAt { get; set; }
        public string ResolvedBySid { get; set; }
    }
}
