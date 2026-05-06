using System;

namespace EZKPM.Shared.Contracts
{
    public class ReportSecurityAlertDto
    {
        public string PackageName { get; set; }
        public string Version { get; set; }
        public string Details { get; set; }
    }

    public class SecurityAlertResponseDto
    {
        public Guid Id { get; set; }
        public string PackageName { get; set; }
        public string Version { get; set; }
        public string Details { get; set; }
        public bool IsResolved { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ResolvedAt { get; set; }
        public string ResolvedBySid { get; set; }
    }
}
