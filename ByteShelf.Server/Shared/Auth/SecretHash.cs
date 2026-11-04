using System.Security.Cryptography;
using System.Text;

namespace ByteShelf.Server.Shared.Auth
{
    /// <summary>How a high-entropy secret (a shelf key, a reset token) is stored and looked up in its place.</summary>
    public static class SecretHash
    {
        /// <summary>
        /// The lowercase-hex SHA-256 of the secret's UTF-8 bytes. A fast hash is enough because the
        /// secrets it is used for are random, unlike passwords.
        /// </summary>
        public static string Sha256Hex(string secret)
        {
            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
        }
    }
}
