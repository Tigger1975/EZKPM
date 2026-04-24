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

    /// <summary>
    /// Das Klartext-Objekt, das der Client (PEP) lokal ver- und entschlüsselt.
    /// Der Server (PDP) sieht diese Daten NIEMALS im Klartext, sondern nur als CipherBlob.
    /// </summary>
    public class VaultAssetPayload
    {
        [System.Text.Json.Serialization.JsonIgnore]
        public Guid? TransientAssetId { get; set; } // Nur im lokalen RAM für UI-Status

        public string AssetType { get; set; } // Login, Payment, SecureNote
        public string Title { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string Url { get; set; }
        public string Notes { get; set; }

        public PasswordGeneratorConfig PasswordSettings { get; set; } = new PasswordGeneratorConfig();
        public LoginFlowConfig LoginFlow { get; set; } = new LoginFlowConfig();
    }
}
