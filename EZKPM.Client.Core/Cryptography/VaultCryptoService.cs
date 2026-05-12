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

        public ECDsa? IdentityKey { get; private set; } // ECDSA for API Auth

        public VaultCryptoService(HybridPqcKeyWrapper keyWrapper)
        {
            _keyWrapper = keyWrapper;
        }

        public enum CryptoInitResult
        {
            Success,
            WrongPassword,
            NotPaired
        }

        public CryptoInitResult InitializeFromStorage(
            string? adBlob, 
            string? tpmBlob, 
            string masterPassword, 
            Func<byte[], byte[]>? tpmProtect,
            Func<byte[], byte[]>? tpmUnprotect,
            out string? newAdBlob, 
            out string? newTpmBlob, 
            out string? newPublicKeyBase64)
        {
            newAdBlob = null;
            newTpmBlob = null;
            newPublicKeyBase64 = null;
            string effectivePassword = string.IsNullOrEmpty(masterPassword) ? "EZKPM_SEAMLESS_SSO" : masterPassword;

            // 1. Try local Blob First (Tier 2)
            if (!string.IsNullOrEmpty(tpmBlob))
            {
                bool expectTpmLocally = tpmUnprotect != null;
                if (DecryptKeysFromBlob(tpmBlob, effectivePassword, expectTpmLocally, tpmUnprotect))
                    return CryptoInitResult.Success;
            }

            // 2. Try AD Blob Fallback (Tier 3)
            if (!string.IsNullOrEmpty(adBlob))
            {
                if (DecryptKeysFromBlob(adBlob, effectivePassword, false, null))
                {
                    // Success with AD! Let's optionally re-generate TPM if it was missing or corrupted
                    if (tpmProtect != null)
                    {
                        byte[] x25519Mat = _myPrivateKeyX25519!.Span.ToArray();
                        byte[] kyberMat = _myPrivateKeyKyber!.Span.ToArray();
                        byte[] ecdsaPriv = IdentityKey!.ExportECPrivateKey();
                        newTpmBlob = EncryptKeysToBlob(effectivePassword, x25519Mat, kyberMat, ecdsaPriv, true, tpmProtect);
                        CryptographicOperations.ZeroMemory(x25519Mat);
                        CryptographicOperations.ZeroMemory(kyberMat);
                        CryptographicOperations.ZeroMemory(ecdsaPriv);
                    }
                    return CryptoInitResult.Success;
                }
            }

            // 3. Both failed or no blobs exist
            if (string.IsNullOrEmpty(adBlob) && string.IsNullOrEmpty(tpmBlob))
            {
                return CryptoInitResult.NotPaired;
            }

            return CryptoInitResult.WrongPassword;
        }

        public void GenerateAndStoreNewKeys(
            string masterPassword,
            Func<byte[], byte[]>? tpmProtect,
            out string newAdBlob, 
            out string? newTpmBlob, 
            out string newPublicKeyBase64)
        {
            newTpmBlob = null;
            string effectivePassword = string.IsNullOrEmpty(masterPassword) ? "EZKPM_SEAMLESS_SSO" : masterPassword;

            byte[] x25519Mat = new byte[32];
            byte[] kyberMat = new byte[32];
            RandomNumberGenerator.Fill(x25519Mat);
            RandomNumberGenerator.Fill(kyberMat);

            _myPrivateKeyX25519 = new SecureMemory(x25519Mat);
            _myPrivateKeyKyber = new SecureMemory(kyberMat);
            
            IdentityKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            byte[] ecdsaPriv = IdentityKey.ExportECPrivateKey();

            newPublicKeyBase64 = Convert.ToBase64String(IdentityKey.ExportSubjectPublicKeyInfo());

            newAdBlob = EncryptKeysToBlob(effectivePassword, x25519Mat, kyberMat, ecdsaPriv, false, null);
            
            if (tpmProtect != null)
            {
                newTpmBlob = EncryptKeysToBlob(effectivePassword, x25519Mat, kyberMat, ecdsaPriv, true, tpmProtect);
            }
            else
            {
                newTpmBlob = EncryptKeysToBlob(effectivePassword, x25519Mat, kyberMat, ecdsaPriv, false, null);
            }
            
            CryptographicOperations.ZeroMemory(x25519Mat);
            CryptographicOperations.ZeroMemory(kyberMat);
            CryptographicOperations.ZeroMemory(ecdsaPriv);
        }

        private string EncryptKeysToBlob(string password, byte[] x25519, byte[] kyber, byte[] ecdsaPriv, bool useTpm, Func<byte[], byte[]>? tpmProtect)
        {
            byte[] salt = new byte[16];
            RandomNumberGenerator.Fill(salt);

            using var argon2 = new Konscious.Security.Cryptography.Argon2id(Encoding.UTF8.GetBytes(password))
            {
                Salt = salt, DegreeOfParallelism = 4, Iterations = 3, MemorySize = 65536
            };
            byte[] kek = argon2.GetBytes(32);

            byte[] hardwarePepperEncrypted = new byte[0];
            if (useTpm && tpmProtect != null)
            {
                byte[] hardwarePepper = new byte[32];
                RandomNumberGenerator.Fill(hardwarePepper);
                hardwarePepperEncrypted = tpmProtect(hardwarePepper);

                // XOR KEK with hardware Pepper
                for (int i = 0; i < 32; i++)
                {
                    kek[i] ^= hardwarePepper[i];
                }
                CryptographicOperations.ZeroMemory(hardwarePepper);
            }

            // Payload: X25519 (32) + Kyber (32) + ECDSA
            byte[] payload = new byte[64 + ecdsaPriv.Length];
            Buffer.BlockCopy(x25519, 0, payload, 0, 32);
            Buffer.BlockCopy(kyber, 0, payload, 32, 32);
            Buffer.BlockCopy(ecdsaPriv, 0, payload, 64, ecdsaPriv.Length);

            byte[] nonce = new byte[12];
            RandomNumberGenerator.Fill(nonce);
            
            byte[] ciphertext = new byte[payload.Length];
            byte[] tag = new byte[16];

            using (var aesGcm = new AesGcm(kek, 16)) { aesGcm.Encrypt(nonce, payload, ciphertext, tag); }

            CryptographicOperations.ZeroMemory(kek);
            CryptographicOperations.ZeroMemory(payload);

            // Format: UseTpmByte(1) + Salt(16) + Nonce(12) + Tag(16) + PepperLen(4) + PepperEncrypted + Ciphertext
            using var ms = new System.IO.MemoryStream();
            using var bw = new System.IO.BinaryWriter(ms);
            bw.Write(useTpm ? (byte)1 : (byte)0);
            bw.Write(salt);
            bw.Write(nonce);
            bw.Write(tag);
            bw.Write(hardwarePepperEncrypted.Length);
            if (hardwarePepperEncrypted.Length > 0) bw.Write(hardwarePepperEncrypted);
            bw.Write(ciphertext);

            return Convert.ToBase64String(ms.ToArray());
        }

        private bool DecryptKeysFromBlob(string encryptedBlobBase64, string password, bool expectTpm, Func<byte[], byte[]>? tpmUnprotect)
        {
            try
            {
                byte[] blob = Convert.FromBase64String(encryptedBlobBase64);
                using var ms = new System.IO.MemoryStream(blob);
                using var br = new System.IO.BinaryReader(ms);

                bool usesTpm = br.ReadByte() == 1;
                if (usesTpm != expectTpm) return false;

                byte[] salt = br.ReadBytes(16);
                byte[] nonce = br.ReadBytes(12);
                byte[] tag = br.ReadBytes(16);
                
                int pepperLen = br.ReadInt32();
                byte[] hardwarePepperEncrypted = br.ReadBytes(pepperLen);
                
                byte[] ciphertext = br.ReadBytes((int)(ms.Length - ms.Position));

                using var argon2 = new Konscious.Security.Cryptography.Argon2id(Encoding.UTF8.GetBytes(password))
                {
                    Salt = salt, DegreeOfParallelism = 4, Iterations = 3, MemorySize = 65536
                };
                byte[] kek = argon2.GetBytes(32);

                if (usesTpm && tpmUnprotect != null)
                {
                    byte[] hardwarePepper = tpmUnprotect(hardwarePepperEncrypted);
                    if (hardwarePepper == null || hardwarePepper.Length != 32) return false;

                    for (int i = 0; i < 32; i++)
                    {
                        kek[i] ^= hardwarePepper[i];
                    }
                    CryptographicOperations.ZeroMemory(hardwarePepper);
                }

                byte[] payload = new byte[ciphertext.Length];
                using (var aesGcm = new AesGcm(kek, 16)) { aesGcm.Decrypt(nonce, ciphertext, tag, payload); }
                CryptographicOperations.ZeroMemory(kek);

                byte[] x25519Mat = new byte[32];
                byte[] kyberMat = new byte[32];
                byte[] ecdsaPriv = new byte[payload.Length - 64];

                Buffer.BlockCopy(payload, 0, x25519Mat, 0, 32);
                Buffer.BlockCopy(payload, 32, kyberMat, 0, 32);
                Buffer.BlockCopy(payload, 64, ecdsaPriv, 0, ecdsaPriv.Length);
                CryptographicOperations.ZeroMemory(payload);

                _myPrivateKeyX25519 = new SecureMemory(x25519Mat);
                _myPrivateKeyKyber = new SecureMemory(kyberMat);
                
                IdentityKey = ECDsa.Create();
                IdentityKey.ImportECPrivateKey(ecdsaPriv, out _);

                CryptographicOperations.ZeroMemory(x25519Mat);
                CryptographicOperations.ZeroMemory(kyberMat);
                CryptographicOperations.ZeroMemory(ecdsaPriv);

                return true;
            }
            catch
            {
                return false;
            }
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
