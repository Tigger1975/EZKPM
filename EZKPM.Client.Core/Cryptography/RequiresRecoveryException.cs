using System;

namespace EZKPM.Client.Core.Cryptography
{
    public class RequiresRecoveryException : Exception
    {
        public RequiresRecoveryException(string message) : base(message) { }
    }
}
