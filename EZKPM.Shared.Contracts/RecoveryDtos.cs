using System;
using System.Collections.Generic;

namespace EZKPM.Shared.Contracts
{
    public class SetupRecoveryDto
    {
        public string HashedSid { get; set; }
        public string EncryptedMasterKeyBackup { get; set; }
    }

    public class InitiateRecoveryRequestDto
    {
        public string HashedSid { get; set; }
        public string EphemeralUserPubKey { get; set; }
    }

    public class ProvideRecoveryShareDto
    {
        public Guid RecoveryRequestId { get; set; }
        public string AdminHashedSid { get; set; }
        public string EncryptedShareBlob { get; set; }
    }

    public class RecoveryStatusResponseDto
    {
        public Guid RecoveryRequestId { get; set; }
        public string HashedSid { get; set; }
        public string EncryptedMasterKeyBackup { get; set; }
        public int RequiredShares { get; set; }
        public bool IsCompleted { get; set; }
        public List<string> EncryptedShareBlobs { get; set; } = new List<string>();
    }
}

