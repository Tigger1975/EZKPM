using System;
using System.Security.Cryptography;
using System.Text;
using Xunit;
using EZKPM.Client.Core.Security;

namespace EZKPM.Client.Tests.Security
{
    /// <summary>
    /// Security Audits für die Krypto-Engine und Anti-Forensik-Maßnahmen (FA 4.3).
    /// </summary>
    public class CryptoEngineTests
    {
        [Fact]
        public void SecureMemory_ShouldWipeOriginalArray_OnInitialization()
        {
            // Arrange
            byte[] secretData = Encoding.UTF8.GetBytes("SuperSecretMasterPassword123!");
            byte[] copyForComparison = new byte[secretData.Length];
            Buffer.BlockCopy(secretData, 0, copyForComparison, 0, secretData.Length);

            // Act
            using var secureMemory = new SecureMemory(secretData);

            // Assert
            // BERECHTIGUNG FA 4.3: Das Original-Array muss beim Übertragen in den SecureMemory 
            // sofort genullt worden sein, um RAM-Forensik zu verhindern.
            Assert.NotEqual(copyForComparison, secretData);
            Assert.All(secretData, b => Assert.Equal(0, b));

            // Der gepinnte Span im SecureMemory muss aber die korrekten, ursprünglichen Daten halten
            Assert.True(secureMemory.Span.SequenceEqual(copyForComparison));
        }

        [Fact]
        public void SecureMemory_ShouldThrowException_WhenAccessedAfterDispose()
        {
            // Arrange
            byte[] data = new byte[] { 1, 2, 3 };
            var secureMemory = new SecureMemory(data);

            // Act
            secureMemory.Dispose();

            // Assert
            // Verhindert versehentliche Nutzung von gelöschtem Speicher im PEP.
            // C# Fix: ReadOnlySpan<byte> (ref struct) kann nicht als object geboxed werden. 
            // Zuweisung an Discard (_) erzwingt die Auswertung ohne Boxing-Versuch.
            Assert.Throws<ObjectDisposedException>(() => { var _ = secureMemory.Span; });
        }

        [Fact]
        public void VaultCryptoService_ShouldEncryptAndDecryptCorrectly()
        {
            // Arrange
            var keyWrapper = new EZKPM.Client.Core.Cryptography.HybridPqcKeyWrapper();
            var cryptoService = new EZKPM.Client.Core.Cryptography.VaultCryptoService(keyWrapper);
            
            var payload = new EZKPM.Shared.Contracts.VaultAssetPayload 
            { 
                Title = "Test Asset", 
                Password = "SuperSecretPassword123" 
            };

            // Act
            var createRequest = cryptoService.EncryptAsset(payload);
            
            var responseDto = new EZKPM.Shared.Contracts.VaultAssetResponseDto
            {
                AssetId = Guid.NewGuid(),
                CipherBlob = createRequest.CipherBlob,
                Nonce = createRequest.Nonce,
                EncryptedKeyShare = createRequest.EncryptedKeyShare
            };
            
            var decryptedPayload = cryptoService.DecryptAsset(responseDto);

            // Assert
            Assert.NotNull(createRequest.CipherBlob);
            Assert.NotNull(createRequest.MetadataHash);
            Assert.Equal(payload.Password, decryptedPayload.Password);
            Assert.Equal(payload.Title, decryptedPayload.Title);
        }

        [Fact]
        public void VaultCryptoService_ShouldFail_WithInvalidTagOrNonce()
        {
            // Arrange
            var keyWrapper = new EZKPM.Client.Core.Cryptography.HybridPqcKeyWrapper();
            var cryptoService = new EZKPM.Client.Core.Cryptography.VaultCryptoService(keyWrapper);
            
            var payload = new EZKPM.Shared.Contracts.VaultAssetPayload { Password = "SecretData" };
            var createRequest = cryptoService.EncryptAsset(payload);

            var responseDto = new EZKPM.Shared.Contracts.VaultAssetResponseDto
            {
                AssetId = Guid.NewGuid(),
                CipherBlob = createRequest.CipherBlob,
                Nonce = createRequest.Nonce,
                EncryptedKeyShare = createRequest.EncryptedKeyShare
            };

            // Act & Assert
            // 1. Auth-Tag manipulieren (letztes Byte im Blob ändern)
            byte[] cipherBytes = Convert.FromBase64String(responseDto.CipherBlob);
            cipherBytes[^1] ^= 0xFF;
            responseDto.CipherBlob = Convert.ToBase64String(cipherBytes);
            
            Assert.ThrowsAny<CryptographicException>(() => cryptoService.DecryptAsset(responseDto));
        }

        [Fact]
        public void VaultCryptoService_ShouldGenerateValidAuditLogChain_FA42()
        {
            // Arrange
            var keyWrapper = new EZKPM.Client.Core.Cryptography.HybridPqcKeyWrapper();
            var cryptoService = new EZKPM.Client.Core.Cryptography.VaultCryptoService(keyWrapper);
            Guid assetId = Guid.NewGuid();
            
            // Act
            byte[] initialHash = new byte[32];
            var log1 = cryptoService.CreateAuditLogRequest("Erster Zugriff", initialHash);
            
            byte[] prevHash = Convert.FromBase64String(log1.CurrentEntryHash);
            var log2 = cryptoService.CreateAuditLogRequest("Zweiter Zugriff", prevHash);

            // Assert
            Assert.NotNull(log1.EncryptedLogBlob);
            Assert.NotNull(log1.CurrentEntryHash);
            
            // Log 2 muss den CurrentEntryHash von Log 1 als PreviousEntryHash verwenden!
            Assert.Equal(log1.CurrentEntryHash, log2.PreviousEntryHash);
        }

        [Fact]
        public void HybridPqcKeyWrapper_ShouldWrapAndUnwrap_SymmetricKeys()
        {
            // Arrange (FA 13 / PQC-Kryptografie Test)
            var wrapper = new EZKPM.Client.Core.Cryptography.HybridPqcKeyWrapper();
            
            // Dummy Asymmetric Keys (for PoC)
            byte[] dummyPubKeyX = new byte[32];
            byte[] dummyPubKeyK = new byte[32];
            byte[] dummyPrivKeyX = new byte[32];
            byte[] dummyPrivKeyK = new byte[32];
            
            using var privX = new EZKPM.Client.Core.Security.SecureMemory(dummyPrivKeyX);
            using var privK = new EZKPM.Client.Core.Security.SecureMemory(dummyPrivKeyK);

            // AES-GCM Key (32 bytes)
            byte[] originalAssetKeyRaw = new byte[32];
            RandomNumberGenerator.Fill(originalAssetKeyRaw);
            using var originalAssetKey = new EZKPM.Client.Core.Security.SecureMemory(originalAssetKeyRaw);

            // Act
            byte[] wrappedBlob = wrapper.WrapAssetKey(originalAssetKey, dummyPubKeyX, dummyPubKeyK);
            
            using var unwrappedKey = wrapper.UnwrapAssetKey(wrappedBlob, privX, privK);

            // Assert
            Assert.NotNull(wrappedBlob);
            Assert.True(wrappedBlob.Length > originalAssetKeyRaw.Length); // Sollte Header, Ciphertext und PQC-Overhead enthalten
            
            Assert.Equal(originalAssetKeyRaw.Length, unwrappedKey.Span.Length);
            Assert.True(unwrappedKey.Span.SequenceEqual(originalAssetKeyRaw));
        }

        [Fact]
        public void HybridPqcKeyWrapper_ShouldFailUnwrap_WhenBlobCorrupted()
        {
            // Arrange
            var wrapper = new EZKPM.Client.Core.Cryptography.HybridPqcKeyWrapper();
            
            byte[] dummyPubKeyX = new byte[32];
            byte[] dummyPubKeyK = new byte[32];
            byte[] dummyPrivKeyX = new byte[32];
            byte[] dummyPrivKeyK = new byte[32];
            
            using var privX = new EZKPM.Client.Core.Security.SecureMemory(dummyPrivKeyX);
            using var privK = new EZKPM.Client.Core.Security.SecureMemory(dummyPrivKeyK);

            // AES-GCM Key (32 bytes)
            byte[] originalAssetKeyRaw = new byte[32];
            RandomNumberGenerator.Fill(originalAssetKeyRaw);
            using var originalAssetKey = new EZKPM.Client.Core.Security.SecureMemory(originalAssetKeyRaw);

            // Act
            byte[] wrappedBlob = wrapper.WrapAssetKey(originalAssetKey, dummyPubKeyX, dummyPubKeyK);
            
            // Corrupt the blob (modify the last byte, which is likely part of the MAC or ciphertext)
            wrappedBlob[^1] ^= 0xFF;

            // Assert
            Assert.ThrowsAny<Exception>(() => wrapper.UnwrapAssetKey(wrappedBlob, privX, privK));
        }
    }
}