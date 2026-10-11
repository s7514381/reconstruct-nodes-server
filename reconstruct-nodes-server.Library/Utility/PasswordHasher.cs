#nullable enable
using System;
using System.Security.Cryptography;

namespace ReconstructNodesServer.Utility
{
    /// <summary>
    /// 帳號密碼雜湊。儲存格式為 [salt(16)][hash(32)]，以 PBKDF2-SHA256 計算。
    /// </summary>
    public static class PasswordHasher
    {
        private const int SaltSize = 16;
        private const int HashSize = 32;
        private const int Iterations = 100_000;

        public static byte[] Hash(string password)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
            byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);

            byte[] result = new byte[SaltSize + HashSize];
            salt.CopyTo(result, 0);
            hash.CopyTo(result, SaltSize);
            return result;
        }

        public static bool Verify(string password, byte[]? stored)
        {
            if (stored == null || stored.Length != SaltSize + HashSize) { return false; }

            byte[] salt = stored[..SaltSize];
            byte[] expected = stored[SaltSize..];
            byte[] actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);

            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
    }
}
