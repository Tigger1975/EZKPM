using System;
using System.Security.Cryptography;
using Xunit;
using EZKPM.Client.Core.Security;
using Moq;

namespace EZKPM.Client.Tests.Security
{
    public class ComplianceTests
    {
        [Fact]
        public void Fido2_Derivation_ShouldFail_WithoutHardwareInteraction()
        {
            // FA 12: Derivation needs physical FIDO2 interaction
            var mockFido = new Mock<IFido2HardwareKey>();
            mockFido.Setup(f => f.GetHmacSecret(It.IsAny<byte[]>(), It.IsAny<byte[]>()))
                    .Returns((byte[])null);

            var kdService = new KeyDerivationService(mockFido.Object);
            using var pwd = new SecureMemory(new byte[] { 1, 2, 3 });

            Assert.Throws<CryptographicException>(() => 
            {
                var key = kdService.DeriveMasterKey(pwd, new byte[32], new byte[32]);
            });
        }

        [Fact]
        public void Fido2_Derivation_ShouldZeroHmacSecret()
        {
            // Verifies that the sensitive FIDO2 HMAC secret is cleared from RAM after use
            byte[] hmacSecret = new byte[32];
            for (int i=0; i<32; i++) hmacSecret[i] = 7;
            
            var mockFido = new Mock<IFido2HardwareKey>();
            mockFido.Setup(f => f.GetHmacSecret(It.IsAny<byte[]>(), It.IsAny<byte[]>()))
                    .Returns(hmacSecret);

            var kdService = new KeyDerivationService(mockFido.Object);
            using var pwd = new SecureMemory(new byte[32]);

            using var masterKey = kdService.DeriveMasterKey(pwd, new byte[32], new byte[32]);

            // hmacSecret array should be zeroed out
            Assert.All(hmacSecret, b => Assert.Equal(0, b));
        }
    }
}
