using System.Buffers.Binary;
using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using ByteShelf.Server.Shared.Root;

namespace ByteShelf.Server.Shared.Auth
{
    /// <summary>
    /// The tokens of links that work without signing in: what a link grants, when it runs out, and an
    /// HMAC-SHA256 over both. One secret under the root's <c>keys/</c> signs them all, through a key
    /// derived from each kind's purpose, so a token of one kind never reads as another; a kind's purpose
    /// must never change, or every link of that kind made before ends. Removing the secret file and
    /// restarting ends every link at once.
    /// </summary>
    public sealed class LinkTokens
    {
        public const string SecretFileName = "link-secret";

        /// <summary>No link's token is longer; a longer one is refused before it is decoded.</summary>
        private const int MaxTokenBytes = 8192;

        private const int ExpiryBytes = sizeof(long);
        private const int SignatureBytes = HMACSHA256.HashSizeInBytes;

        private readonly byte[] _secret;
        private readonly TimeProvider _timeProvider;

        public LinkTokens(RootFolder root, TimeProvider timeProvider)
        {
            _secret = Convert.FromBase64String(RootKeyMaterial.GetOrCreate(root, SecretFileName, byteCount: 32));
            _timeProvider = timeProvider;
        }

        /// <summary>
        /// A token of the kind <paramref name="purpose"/> granting <paramref name="payload"/> for
        /// <paramref name="lifetime"/>. It keeps whole seconds, so it runs out at the second the
        /// lifetime ends in, which is the time it answers.
        /// </summary>
        public (string Token, DateTimeOffset ExpiresAt) Create(string purpose, ReadOnlySpan<byte> payload, TimeSpan lifetime)
        {
            long expiresAtSeconds = (_timeProvider.GetUtcNow() + lifetime).ToUnixTimeSeconds();
            byte[] token = new byte[ExpiryBytes + payload.Length + SignatureBytes];
            BinaryPrimitives.WriteInt64BigEndian(token.AsSpan(0, ExpiryBytes), expiresAtSeconds);
            payload.CopyTo(token.AsSpan(ExpiryBytes));

            int signed = ExpiryBytes + payload.Length;
            HMACSHA256.HashData(KeyFor(purpose), token.AsSpan(0, signed), token.AsSpan(signed));

            return (Base64Url.EncodeToString(token), DateTimeOffset.FromUnixTimeSeconds(expiresAtSeconds));
        }

        /// <summary>What a token of the kind <paramref name="purpose"/> grants, when it is one and has not run out.</summary>
        public bool TryRead(string purpose, string token, [NotNullWhen(true)] out byte[]? payload)
        {
            payload = null;

            // TryDecodeFromChars throws on a character outside the alphabet rather than answering false.
            if (!Base64Url.IsValid(token, out int length) || length < ExpiryBytes + SignatureBytes || length > MaxTokenBytes)
            {
                return false;
            }

            byte[] bytes = new byte[length];
            if (!Base64Url.TryDecodeFromChars(token, bytes, out int written) || written != length)
            {
                return false;
            }

            int signed = length - SignatureBytes;
            Span<byte> expected = stackalloc byte[SignatureBytes];
            HMACSHA256.HashData(KeyFor(purpose), bytes.AsSpan(0, signed), expected);
            if (!CryptographicOperations.FixedTimeEquals(expected, bytes.AsSpan(signed)))
            {
                return false;
            }

            long expiresAt = BinaryPrimitives.ReadInt64BigEndian(bytes.AsSpan(0, ExpiryBytes));
            if (_timeProvider.GetUtcNow().ToUnixTimeSeconds() >= expiresAt)
            {
                return false;
            }

            payload = bytes[ExpiryBytes..signed];
            return true;
        }

        private byte[] KeyFor(string purpose)
        {
            return HMACSHA256.HashData(_secret, Encoding.UTF8.GetBytes(purpose));
        }
    }
}
