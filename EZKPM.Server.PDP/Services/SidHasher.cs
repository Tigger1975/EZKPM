using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace EZKPM.Server.PDP.Services
{
    public static class SidHasher
    {
        private static byte[] _pepper;

        static SidHasher()
        {
            var pepperPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sid_pepper.key");
            if (File.Exists(pepperPath))
            {
                _pepper = File.ReadAllBytes(pepperPath);
            }
            else
            {
                _pepper = new byte[32];
                RandomNumberGenerator.Fill(_pepper);
                File.WriteAllBytes(pepperPath, _pepper);
            }
        }

        public static string HashSid(string sid)
        {
            if (string.IsNullOrWhiteSpace(sid)) return sid;
            
            // If already a base64 hash (44 chars), return as is
            if (sid.Length == 44 && sid.EndsWith("=")) return sid;

            using var hmac = new HMACSHA256(_pepper);
            var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(sid.Trim().ToUpperInvariant()));
            return Convert.ToBase64String(hashBytes);
        }
    }
}
