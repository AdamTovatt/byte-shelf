using System.Security.Cryptography;
using System.Text;

namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>Hashes as the server stores them, for tests that check a file's SHA-256.</summary>
    public static class TestHashes
    {
        /// <summary>The lowercase hex SHA-256 of <paramref name="text"/> as UTF-8.</summary>
        public static string Sha256Of(string text)
        {
            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        }
    }
}
