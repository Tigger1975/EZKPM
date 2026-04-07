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
            var cryptoService = new VaultCryptoService();
            string plainText = "MySecretCreditCardData";

            byte[] keyBytes = new byte[32]; // 256-bit (AES-256)
            RandomNumberGenerator.Fill(keyBytes);
            using var key = new SecureMemory(keyBytes);

            // Act
            var (cipherBlob, nonce) = cryptoService.EncryptAssetPayload(plainText, key);
            string decryptedText = cryptoService.DecryptAssetPayload(cipherBlob, nonce, key);

            // Assert
            Assert.NotNull(cipherBlob);
            Assert.Equal(12, nonce.Length); // GCM Nonce muss 96 Bit haben
            Assert.True(cipherBlob.Length > plainText.Length); // Chiffrat + 16 Byte Auth-Tag
            Assert.Equal(plainText, decryptedText);
        }

        [Fact]
        public void VaultCryptoService_ShouldFail_WithInvalidTagOrNonce()
        {
            // Arrange
            var cryptoService = new VaultCryptoService();
            byte[] keyBytes = new byte[32];
            RandomNumberGenerator.Fill(keyBytes);
            using var key = new SecureMemory(keyBytes);

            var (cipherBlob, nonce) = cryptoService.EncryptAssetPayload("ConfidentialData", key);

            // Act & Assert
            // 1. Auth-Tag manipulieren (letztes Byte im Blob ändern)
            // Simuliert einen Man-in-the-Middle oder korrupten Datenbankeintrag.
            cipherBlob[^1] ^= 0xFF;
            Assert.ThrowsAny<CryptographicException>(() => cryptoService.DecryptAssetPayload(cipherBlob, nonce, key));

            // 2. Nonce manipulieren
            cipherBlob[^1] ^= 0xFF; // Auth-Tag zurücksetzen
            nonce[0] ^= 0xFF;
            Assert.ThrowsAny<CryptographicException>(() => cryptoService.DecryptAssetPayload(cipherBlob, nonce, key));
        }
    }
}