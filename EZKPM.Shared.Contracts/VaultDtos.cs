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
        
        public List<AclEntryDto> Acls { get; set; } = new();
    }

    public class AclEntryDto
    {
        public string AdSid { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public int PermissionLevel { get; set; } // 1=Execute, 2=Read, 3=Owner
        public string EncryptedKeyShare { get; set; } = "";
    }

    public class AuditLogRequestDto
    {
        public string EncryptedLogBlob { get; set; }
        public string Nonce { get; set; }
        public string PreviousEntryHash { get; set; }
        public string CurrentEntryHash { get; set; }
    }

    public class PasswordGeneratorConfig
    {
        public int Length { get; set; } = 20;
        public bool UseUppercase { get; set; } = true;
        public bool UseLowercase { get; set; } = true;
        public bool UseNumbers { get; set; } = true;
        public bool UseSymbols { get; set; } = true;
    }

    public class LoginFlowConfig
    {
        public string Method { get; set; } = "AutoLearn"; // AutoLearn, OneStep, TwoStep, BasicAuth
        public bool AutoLearnEnabled { get; set; } = true;
        
        // Gespeicherte DOM-Ergebnisse vom AutoLearn
        public string UsernameSelector { get; set; }
        public string PasswordSelector { get; set; }
        public string NextButtonSelector { get; set; }
        public string SubmitButtonSelector { get; set; }
    }

    public class CustomField
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public string Description { get; set; }
        public bool IsSecret { get; set; }
    }

    public class VaultAttachment
    {
        public string FileName { get; set; }
        public byte[] FileData { get; set; }
    }

    /// <summary>
    /// Das Klartext-Objekt, das der Client (PEP) lokal ver- und entschlüsselt.
    /// Der Server (PDP) sieht diese Daten NIEMALS im Klartext, sondern nur als CipherBlob.
    /// </summary>
    public class VaultAssetPayload
    {
        [System.Text.Json.Serialization.JsonIgnore]
        public Guid? TransientAssetId { get; set; } // Nur im lokalen RAM für UI-Status
        
        [System.Text.Json.Serialization.JsonIgnore]
        public string FullPath { get; set; } // Computed path for display

        public Guid? ParentFolderId { get; set; } // Für die Tree-Struktur

        public string AssetType { get; set; } // Folder, Login, Payment, SecureNote
        public string Title { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string Url { get; set; }
        public string Notes { get; set; }
        public string DetailedDescription { get; set; } // Genauere Beschreibung
        public string TotpSecret { get; set; } // Base32 Secret für Authenticator

        public int PasswordValidityDays { get; set; } = 365; // Max 1 Jahr

        // Payment fields
        public string PaymentSubType { get; set; } = "Card"; // "Card" or "Service"
        public string CardHolder { get; set; }
        public string CardExpiry { get; set; }
        public string CardCvc { get; set; }

        // File upload fields for SSH, SSL, Passkeys, Certificates
        public string FileUploadName { get; set; }
        public byte[] FileUploadData { get; set; }

        public string FileUploadName2 { get; set; }
        public byte[] FileUploadData2 { get; set; }

        public System.Collections.Generic.List<CustomField> CustomFields { get; set; } = new();
        
        // Attached Files / Binaries
        public List<VaultAttachment> Attachments { get; set; } = new();
        
        // Access Control List (For UI Display)
        public List<AclEntryDto> Acls { get; set; } = new();

        public PasswordGeneratorConfig PasswordSettings { get; set; } = new PasswordGeneratorConfig();
        public LoginFlowConfig LoginFlow { get; set; } = new LoginFlowConfig();
    }
}
