using System;

namespace EZKPM.Shared.Contracts
{
    public class UpdateCheckResponseDto
    {
        public bool UpdateAvailable { get; set; }
        public string LatestVersion { get; set; }
        public string DownloadUrl { get; set; }
        public string ReleaseNotes { get; set; }
        public bool IsMandatory { get; set; }
    }
}
