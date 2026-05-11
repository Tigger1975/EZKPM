#nullable enable
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
        
        // True private keys securely loaded via DPAPI + Password (Argon2id)
        private SecureMemory? _myPrivateKeyX25519;
        private SecureMemory? _myPrivateKeyKyber;
        private readonly byte[] _testPreviousHash = new byte[32]; // Genesis block

        public VaultCryptoService(HybridPqcKeyWrapper keyWrapper)
        {
            _keyWrapper = keyWrapper;
        }

        public bool Initialize(string masterPassword)
        {
            byte[] machineSecret = DpapiMasterKeyStore.GetOrGenerateMachineSecret();
            
            // Argon2 does not accept empty passwords. If seamless SSO is used (empty string), use a static fallback.
            string effectivePassword = string.IsNullOrEmpty(masterPassword) ? "EZKPM_DPAPI_SEAMLESS_SSO" : masterPassword;

            // FA 13: Password + Machine-bound token (DPAPI secret)
            using var argon2 = new Konscious.Security.Cryptography.Argon2id(Encoding.UTF8.GetBytes(effectivePassword))
            {
                Salt = machineSecret,
                DegreeOfParallelism = 4,
                Iterations = 3,
                MemorySize = 65536 // 64 MB
            };
            
            byte[] derivedMaterial = argon2.GetBytes(64);
            
            byte[] x25519Material = new byte[32];
            byte[] kyberMaterial = new byte[32];

            try
            {
                Buffer.BlockCopy(derivedMaterial, 0, x25519Material, 0, 32);
                Buffer.BlockCopy(derivedMaterial, 32, kyberMaterial, 0, 32);

                // Verify key check
                string appDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EZKPM");
                string keyCheckPath = System.IO.Path.Combine(appDir, "keycheck.dat");

                using var sha256 = SHA256.Create();
                byte[] hash = sha256.ComputeHash(derivedMaterial);

                if (System.IO.File.Exists(keyCheckPath))
                {
                    byte[] storedHash = System.IO.File.ReadAllBytes(keyCheckPath);
                    if (!System.Linq.Enumerable.SequenceEqual(hash, storedHash))
                    {
                        return false; // Wrong password
                    }
                }
                else
                {
                    if (!System.IO.Directory.Exists(appDir)) System.IO.Directory.CreateDirectory(appDir);
                    System.IO.File.WriteAllBytes(keyCheckPath, hash);
                }
                
                _myPrivateKeyX25519 = new SecureMemory(x25519Material);
                _myPrivateKeyKyber = new SecureMemory(kyberMaterial);

                return true;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(machineSecret);
                CryptographicOperations.ZeroMemory(derivedMaterial);
                CryptographicOperations.ZeroMemory(x25519Material);
                CryptographicOperations.ZeroMemory(kyberMaterial);
            }
        }

        public string GenerateEncryptedMachineBackup(string masterPassword)
        {
            if (_myPrivateKeyX25519 == null || _myPrivateKeyKyber == null)
                throw new InvalidOperationException("CryptoService is not initialized.");

            string effectivePassword = string.IsNullOrEmpty(masterPassword) ? "EZKPM_DPAPI_SEAMLESS_SSO" : masterPassword;

            // Generate an ephemeral salt for Argon2
            byte[] salt = new byte[16];
            RandomNumberGenerator.Fill(salt);

            using var argon2 = new Konscious.Security.Cryptography.Argon2id(Encoding.UTF8.GetBytes(effectivePassword))
            {
                Salt = salt,
                DegreeOfParallelism = 4,
                Iterations = 3,
                MemorySize = 65536 // 64 MB
            };

            // Derive a 32-byte Key Encryption Key (KEK)
            byte[] kek = argon2.GetBytes(32);

            // Payload: X25519 (32) + Kyber (32) = 64 bytes
            byte[] payload = new byte[64];
            Buffer.BlockCopy(_myPrivateKeyX25519.Span.ToArray(), 0, payload, 0, 32);
            Buffer.BlockCopy(_myPrivateKeyKyber.Span.ToArray(), 0, payload, 32, 32);

            byte[] nonce = new byte[12];
            RandomNumberGenerator.Fill(nonce);
            
            byte[] ciphertext = new byte[payload.Length];
            byte[] tag = new byte[16];

            using (var aesGcm = new AesGcm(kek, 16))
            {
                aesGcm.Encrypt(nonce, payload, ciphertext, tag);
            }

            CryptographicOperations.ZeroMemory(kek);
            CryptographicOperations.ZeroMemory(payload);

            // Format: Salt (16) + Nonce (12) + Ciphertext (64) + Tag (16)
            byte[] blob = new byte[salt.Length + nonce.Length + ciphertext.Length + tag.Length];
            Buffer.BlockCopy(salt, 0, blob, 0, salt.Length);
            Buffer.BlockCopy(nonce, 0, blob, salt.Length, nonce.Length);
            Buffer.BlockCopy(ciphertext, 0, blob, salt.Length + nonce.Length, ciphertext.Length);
            Buffer.BlockCopy(tag, 0, blob, salt.Length + nonce.Length + ciphertext.Length, tag.Length);

            return Convert.ToBase64String(blob);
        }

        public VaultAssetPayload? DecryptAsset(VaultAssetResponseDto assetDto)
        {
            byte[] encryptedKeyShare = Convert.FromBase64String(assetDto.EncryptedKeyShare);
            
            // 1. Unwrap the Asset Key using HybridPqcKeyWrapper
            using var assetKey = _keyWrapper.UnwrapAssetKey(encryptedKeyShare, _myPrivateKeyX25519, _myPrivateKeyKyber);

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

            try
            {
                using (var aesGcm = new AesGcm(assetKey.Span, 16))
                {
                    aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);
                }

                string json = Encoding.UTF8.GetString(plaintext);

                var payload = System.Text.Json.JsonSerializer.Deserialize<VaultAssetPayload>(json);
                if (payload != null) 
                {
                    payload.TransientAssetId = assetDto.AssetId;
                    payload.IsExpired = assetDto.IsExpired;
                    payload.IsDeleted = assetDto.IsDeleted;
                }
                return payload;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }

        public CreateAssetRequestDto EncryptAsset(VaultAssetPayload payload)
        {
            string json = System.Text.Json.JsonSerializer.Serialize(payload);
            byte[] plaintext = Encoding.UTF8.GetBytes(json);

            try
            {
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
                // Use HMAC-SHA256 with a static pepper to prevent dictionary attacks on blind indices
                byte[] pepper = Encoding.UTF8.GetBytes("EZKPM_GLOBAL_PEPPER_9F8A7B6C5D4E3F2A1B0C");
                using var hmac = new HMACSHA256(pepper);
                string metadataString = $"{payload.Url}|{payload.Username}".ToLowerInvariant();
                byte[] metadataHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(metadataString));

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
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
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

        public AuditLogRequestDto CreateAuditLogRequest(string cleartextLog, byte[] previousHash)
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
            byte[] buffer = new byte[previousHash.Length + encryptedBlob.Length];
            Buffer.BlockCopy(previousHash, 0, buffer, 0, previousHash.Length);
            Buffer.BlockCopy(encryptedBlob, 0, buffer, previousHash.Length, encryptedBlob.Length);

            byte[] currentHash = sha256.ComputeHash(buffer);
            
            // Save the old previous hash for the DTO
            string previousHashBase64 = Convert.ToBase64String(previousHash);

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
