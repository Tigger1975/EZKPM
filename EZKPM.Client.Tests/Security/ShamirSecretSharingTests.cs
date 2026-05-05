using System;
using System.Collections.Generic;
using System.Text;
using Xunit;
using EZKPM.Client.Core.Cryptography;

namespace EZKPM.Client.Tests.Security
{
    public class ShamirSecretSharingTests
    {
        [Fact]
        public void ShouldSplitAndRecoverSecret_WithSufficientShares()
        {
            // Arrange
            byte[] secret = Encoding.UTF8.GetBytes("DiesIstEinSichererMasterKey12345");
            int threshold = 3; // 3 aus 5
            int totalShares = 5;

            // Act
            var shares = ShamirSecretSharing.SplitSecret(secret, threshold, totalShares);

            // Assert
            Assert.Equal(5, shares.Count);

            // Recover with 3 random shares
            var recoveryShares = new List<byte[]> { shares[0], shares[2], shares[4] };
            byte[] recoveredSecret = ShamirSecretSharing.RecoverSecret(recoveryShares);

            Assert.Equal(secret, recoveredSecret);
        }

        [Fact]
        public void ShouldRecoverCorrectly_WithExactlyThreshold()
        {
            byte[] secret = new byte[32];
            new Random().NextBytes(secret);
            int threshold = 2;
            int totalShares = 4;

            var shares = ShamirSecretSharing.SplitSecret(secret, threshold, totalShares);
            var recoveryShares = new List<byte[]> { shares[1], shares[3] };
            byte[] recoveredSecret = ShamirSecretSharing.RecoverSecret(recoveryShares);

            Assert.Equal(secret, recoveredSecret);
        }
    }
}
