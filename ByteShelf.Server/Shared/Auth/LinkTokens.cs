using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using ByteShelf.Server.Shared.Root;

namespace ByteShelf.Server.Shared.Auth
{
    /// <summary>
    /// The tokens of links that work without signing in: the link's id and an HMAC-SHA256 over it.
    /// What a link grants and when it runs out are in its row, so deleting the row ends it. One secret
    /// under the root's <c>keys/</c> signs them all, through a key derived from each kind's purpose, so a
    /// token of one kind never reads as another; changing a kind's purpose ends every link of that kind
    /// made before. Removing the secret file and restarting ends every link at once.
    /// </summary>
    public sealed class LinkTokens
    {
        public const string SecretFileName = "link-secret";

        private const int IdBytes = 16;
        private const int SignatureBytes = HMACSHA256.HashSizeInBytes;
        private const int TokenBytes = IdBytes + SignatureBytes;

        private readonly byte[] _secret;

        public LinkTokens(RootFolder root)
        {
            _secret = Convert.FromBase64String(RootKeyMaterial.GetOrCreate(root, SecretFileName, byteCount: 32));
        }

        /// <summary>The token of the kind <paramref name="purpose"/> naming the link <paramref name="linkId"/>.</summary>
        public string Create(string purpose, Guid linkId)
        {
            byte[] token = new byte[TokenBytes];
            linkId.TryWriteBytes(token.AsSpan(0, IdBytes));
            HMACSHA256.HashData(KeyFor(purpose), token.AsSpan(0, IdBytes), token.AsSpan(IdBytes));
            return Base64Url.EncodeToString(token);
        }

        /// <summary>The link a token of the kind <paramref name="purpose"/> names, when it is one.</summary>
        public bool TryRead(string purpose, string token, out Guid linkId)
        {
            linkId = Guid.Empty;

            // TryDecodeFromChars throws on a character outside the alphabet rather than answering false.
            if (!Base64Url.IsValid(token, out int length) || length != TokenBytes)
            {
                return false;
            }

            Span<byte> bytes = stackalloc byte[TokenBytes];
            if (!Base64Url.TryDecodeFromChars(token, bytes, out int written) || written != TokenBytes)
            {
                return false;
            }

            Span<byte> expected = stackalloc byte[SignatureBytes];
            HMACSHA256.HashData(KeyFor(purpose), bytes[..IdBytes], expected);
            if (!CryptographicOperations.FixedTimeEquals(expected, bytes[IdBytes..]))
            {
                return false;
            }

            linkId = new Guid(bytes[..IdBytes]);
            return true;
        }

        private byte[] KeyFor(string purpose)
        {
            return HMACSHA256.HashData(_secret, Encoding.UTF8.GetBytes(purpose));
        }
    }
}
