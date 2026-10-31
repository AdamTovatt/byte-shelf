using System.Buffers.Text;
using System.Security.Cryptography;
using ByteShelf.Server.Shared.Auth;
using ByteShelfCommon;

namespace ByteShelf.Server.Access.Services
{
    /// <summary>Makes key secrets and the hashes stored in their place.</summary>
    public static class ShelfKeySecrets
    {
        private const int SecretBytes = 32;

        /// <summary>A new secret: the prefix and 256 random bits, base64url.</summary>
        public static string Generate()
        {
            return ShelfKeys.SecretPrefix + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(SecretBytes));
        }

        /// <summary>The hash of a secret, as stored and looked up.</summary>
        public static string Hash(string secret)
        {
            return SecretHash.Sha256Hex(secret);
        }
    }
}
