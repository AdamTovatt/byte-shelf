using System.Buffers.Text;
using System.Security.Cryptography;

namespace ByteShelf.Server.Shared.Auth
{
    /// <summary>
    /// The secrets the server hands out (a shelf key, a replica's credential, a pairing code's secret):
    /// 256 random bits, base64url, after a prefix saying what they are. Stored as <see cref="SecretHash"/>.
    /// </summary>
    public static class RandomSecret
    {
        private const int SecretBytes = 32;

        public static string Generate(string prefix = "")
        {
            return prefix + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(SecretBytes));
        }
    }
}
