using System;
using System.Security.Cryptography;
using System.Text;
using EZKPM.Client.Core.Security;
using EZKPM.Shared.Contracts;

namespace EZKPM.Client.Core.Cryptography
{
    public class VaultCryptoService
    {
        private readonly HybridPqcKeyWrapper _keyWrapper;
        
        // Mock private keys for test mode (in reality, bound to FIDO2/TPM)
        private readonly SecureMemory _testPrivateKeyX25519;
        private readonly SecureMemory _testPrivateKeyKyber;
        private readonly byte[] _testPreviousHash = new byte[32]; // Genesis block

        public VaultCryptoService(HybridPqcKeyWrapper keyWrapper)
        {
            _keyWrapper = keyWrapper;
            _testPrivateKeyX25519 = new SecureMemory(new byte[32]);
            _testPrivateKeyKyber = new SecureMemory(new byte[32]);
        }

        public VaultAssetPayload DecryptAsset(VaultAssetResponseDto assetDto)
        {
            byte[] encryptedKeyShare = Convert.FromBase64String(assetDto.EncryptedKeyShare);
            
            // 1. Unwrap the Asset Key using HybridPqcKeyWrapper
            using var assetKey = _keyWrapper.UnwrapAssetKey(encryptedKeyShare, _testPrivateKeyX25519, _testPrivateKeyKyber);

            // 2. Decrypt the CipherBlob using AesGcm
            byte[] cipherBlob = Convert.FromBase64String(assetDto.CipherBlob);
            byte[] nonce = Convert.FromBase64String(assetDto.Nonce);
            
            if (cipherBlob.Length <= 16)
                throw new CryptographicException("Invalid cipher blob size.");

            int ciphertextLength = cipherBlob.Length - 16;
            byte[] ciphertext = new byte[ciphertextLength];
            byte[] tag = new byte[16];
            
            Buffer.BlockCopy(cipherBlob, 0, ciphertext, 0, ciphertextLength);
            Buffer.BlockCopy(cipherBlob, ciphertextLength, tag, 0, 16);

            byte[] plaintext = new byte[ciphertextLength];

            using (var aesGcm = new AesGcm(assetKey.Span, 16))
            {
                aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);
            }

            string json = Encoding.UTF8.GetString(plaintext);
            CryptographicOperations.ZeroMemory(plaintext);

            var payload = System.Text.Json.JsonSerializer.Deserialize<VaultAssetPayload>(json);
            if (payload != null) payload.TransientAssetId = assetDto.AssetId;
            return payload;
        }

        public CreateAssetRequestDto EncryptAsset(VaultAssetPayload payload)
        {
            string json = System.Text.Json.JsonSerializer.Serialize(payload);
            byte[] plaintext = Encoding.UTF8.GetBytes(json);

            // 1. Generate new Asset Key (AES-256)
            byte[] newAssetKeyBytes = new byte[32];
            RandomNumberGenerator.Fill(newAssetKeyBytes);
            using var assetKey = new SecureMemory(newAssetKeyBytes);

            // 2. Encrypt Payload
            byte[] nonce = new byte[12];
            RandomNumberGenerator.Fill(nonce);

            byte[] ciphertext = new byte[plaintext.Length];
            byte[] tag = new byte[16];

            using (var aesGcm = new AesGcm(assetKey.Span, 16))
            {
                aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);
            }

            byte[] cipherBlob = new byte[ciphertext.Length + tag.Length];
            Buffer.BlockCopy(ciphertext, 0, cipherBlob, 0, ciphertext.Length);
            Buffer.BlockCopy(tag, 0, cipherBlob, ciphertext.Length, tag.Length);

            // 3. Wrap Asset Key for the Owner (Self)
            // In reality, this requires the Owner's Public Keys. For the test, we mock them.
            byte[] dummyPubKeyX = new byte[32];
            byte[] dummyPubKeyK = new byte[32];
            byte[] wrappedKey = _keyWrapper.WrapAssetKey(assetKey, dummyPubKeyX, dummyPubKeyK);

            // 4. Compute Metadata Hash (for Server uniqueness checks)
            using var sha256 = SHA256.Create();
            string metadataString = $"{payload.Url}|{payload.Username}".ToLowerInvariant();
            byte[] metadataHash = sha256.ComputeHash(Encoding.UTF8.GetBytes(metadataString));

            CryptographicOperations.ZeroMemory(plaintext);

            return new CreateAssetRequestDto
            {
                MetadataHash = Convert.ToBase64String(metadataHash),
                CipherBlob = Convert.ToBase64String(cipherBlob),
                Nonce = Convert.ToBase64String(nonce),
                ExpiresAt = DateTime.UtcNow.AddDays(payload.PasswordValidityDays > 0 && payload.PasswordValidityDays <= 365 ? payload.PasswordValidityDays : 365), // FA 30
                EncryptedKeyShare = Convert.ToBase64String(wrappedKey),
                Acls = payload.Acls ?? new System.Collections.Generic.List<EZKPM.Shared.Contracts.AclEntryDto>()
            };
        }

        public string GeneratePassword(PasswordGeneratorConfig config = null)
        {
            if (config == null) config = new PasswordGeneratorConfig();

            string pool = "";
            if (config.UseUppercase) pool += "ABCDEFGHJKLMNPQRSTUVWXYZ"; // No I, O
            if (config.UseLowercase) pool += "abcdefghijkmnopqrstuvwxyz"; // No l
            if (config.UseNumbers) pool += "23456789"; // No 0, 1
            if (config.UseSymbols) pool += "!@#$%^&*()-_=+[]{}";

            if (pool.Length == 0) pool = "abcdefghijkmnopqrstuvwxyz23456789";

            var chars = new char[config.Length];
            for (int i = 0; i < config.Length; i++)
            {
                chars[i] = pool[RandomNumberGenerator.GetInt32(pool.Length)];
            }
            
            // Shuffle
            for (int i = chars.Length - 1; i > 0; i--)
            {
                int j = RandomNumberGenerator.GetInt32(i + 1);
                (chars[i], chars[j]) = (chars[j], chars[i]);
            }

            return new string(chars);
        }

        public AuditLogRequestDto CreateAuditLogRequest(string cleartextLog)
        {
            // FA 22 & FA 4.2 implementation
            byte[] logBytes = Encoding.UTF8.GetBytes(cleartextLog);
            byte[] nonce = new byte[12];
            RandomNumberGenerator.Fill(nonce);

            // In reality, this would be the Group Log Key (K_L)
            byte[] logKey = new byte[32]; 
            byte[] ciphertext = new byte[logBytes.Length];
            byte[] tag = new byte[16];

            using (var aesGcm = new AesGcm(logKey, 16))
            {
                aesGcm.Encrypt(nonce, logBytes, ciphertext, tag);
            }

            // Combine ciphertext and tag
            byte[] encryptedBlob = new byte[ciphertext.Length + tag.Length];
            Buffer.BlockCopy(ciphertext, 0, encryptedBlob, 0, ciphertext.Length);
            Buffer.BlockCopy(tag, 0, encryptedBlob, ciphertext.Length, tag.Length);

            // Hash Chaining: CurrentHash = SHA256(PreviousHash + EncryptedBlob)
            using var sha256 = SHA256.Create();
            byte[] buffer = new byte[_testPreviousHash.Length + encryptedBlob.Length];
            Buffer.BlockCopy(_testPreviousHash, 0, buffer, 0, _testPreviousHash.Length);
            Buffer.BlockCopy(encryptedBlob, 0, buffer, _testPreviousHash.Length, encryptedBlob.Length);

            byte[] currentHash = sha256.ComputeHash(buffer);
            
            // Save the old previous hash for the DTO
            string previousHashBase64 = Convert.ToBase64String(_testPreviousHash);

            // Update the local hash anchor for the next log entry
            Buffer.BlockCopy(currentHash, 0, _testPreviousHash, 0, currentHash.Length);

            CryptographicOperations.ZeroMemory(logBytes);

            return new AuditLogRequestDto
            {
                EncryptedLogBlob = Convert.ToBase64String(encryptedBlob),
                Nonce = Convert.ToBase64String(nonce),
                PreviousEntryHash = previousHashBase64,
                CurrentEntryHash = Convert.ToBase64String(currentHash)
            };
        }
    }
}
