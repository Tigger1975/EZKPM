using System;

namespace EZKPM.Shared.Contracts
{
    public class VaultAssetResponseDto
    {
        public Guid AssetId { get; set; }
        public string CipherBlob { get; set; }
        public string Nonce { get; set; }
        public int PermissionLevel { get; set; }
        public string EncryptedKeyShare { get; set; }
        public bool IsExpired { get; set; }
    }

    public class CreateAssetRequestDto
    {
        public string MetadataHash { get; set; }
        public string CipherBlob { get; set; }
        public string Nonce { get; set; }
        public DateTime ExpiresAt { get; set; }
        
        // The owner's initial encrypted key share
        public string EncryptedKeyShare { get; set; }
    }

    public class AuditLogRequestDto
    {
        public string EncryptedLogBlob { get; set; }
        public string Nonce { get; set; }
        public string PreviousEntryHash { get; set; }
        public string CurrentEntryHash { get; set; }
    }

    /// <summary>
    /// Der Klartext-Payload, der vom Client verschlüsselt in den CipherBlob gelegt wird.
    /// </summary>
    public class VaultAssetPayload
    {
        public string AssetType { get; set; } // "Login", "Payment", "Note"
        public string Title { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string Url { get; set; }
        public string Notes { get; set; }
    }
}
