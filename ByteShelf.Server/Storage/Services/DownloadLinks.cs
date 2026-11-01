using System.Buffers.Binary;
using System.Buffers.Text;
using System.Security.Cryptography;
using ByteShelf.Server.Shared.Root;
using ByteShelfCommon;

namespace ByteShelf.Server.Storage.Services
{
    /// <summary>
    /// Signed links to one file's contents: for a browser that cannot send a token with a plain link
    /// or an image's source, and to share a file with someone who has no access. A link carries the
    /// shelf's and the file's ids and when it runs out, signed with a key kept under the root's
    /// <c>keys/</c>; whoever holds it may read that file until then, even if the access it was made
    /// with is taken away meanwhile. Removing the key file and restarting ends every link at once.
    /// </summary>
    public sealed class DownloadLinks
    {
        public const string SecretFileName = "download-link-secret";

        /// <summary>The route links point at, served by <c>DownloadsController</c>; a link is this, a slash and its token.</summary>
        public const string Route = "api/downloads";

        private const int IdBytes = 16;
        private const int ExpiryBytes = sizeof(long);
        private const int SignedBytes = (2 * IdBytes) + ExpiryBytes;
        private const int SignatureBytes = HMACSHA256.HashSizeInBytes;

        private readonly byte[] _key;
        private readonly TimeProvider _timeProvider;

        public DownloadLinks(RootFolder root, TimeProvider timeProvider)
        {
            _key = Convert.FromBase64String(RootKeyMaterial.GetOrCreate(root, SecretFileName, byteCount: 32));
            _timeProvider = timeProvider;
        }

        /// <summary>
        /// A link to the file that works for <paramref name="lifetime"/>, or for
        /// <see cref="DownloadLink.DefaultLifetime"/> when null. The token keeps whole seconds, so the
        /// link runs out at the second the lifetime ends in, which is the time it answers.
        /// </summary>
        /// <exception cref="ArgumentException">The lifetime is one <see cref="DownloadLink.NormalizeLifetime"/> refuses.</exception>
        public DownloadLink Create(Guid shelfId, Guid fileId, TimeSpan? lifetime)
        {
            long expiresAtSeconds = (_timeProvider.GetUtcNow() + DownloadLink.NormalizeLifetime(lifetime)).ToUnixTimeSeconds();
            byte[] token = new byte[SignedBytes + SignatureBytes];
            shelfId.TryWriteBytes(token.AsSpan(0, IdBytes));
            fileId.TryWriteBytes(token.AsSpan(IdBytes, IdBytes));
            BinaryPrimitives.WriteInt64BigEndian(token.AsSpan(2 * IdBytes, ExpiryBytes), expiresAtSeconds);
            DateTimeOffset expiresAt = DateTimeOffset.FromUnixTimeSeconds(expiresAtSeconds);
            HMACSHA256.HashData(_key, token.AsSpan(0, SignedBytes), token.AsSpan(SignedBytes));

            return new DownloadLink { Url = $"/{Route}/" + Base64Url.EncodeToString(token), ExpiresAt = expiresAt };
        }

        /// <summary>The shelf and file a link's token names, when it is one of these links and has not run out.</summary>
        public bool TryRead(string token, out Guid shelfId, out Guid fileId)
        {
            shelfId = Guid.Empty;
            fileId = Guid.Empty;

            // TryDecodeFromChars throws on a character outside the alphabet rather than answering false.
            byte[] bytes = new byte[SignedBytes + SignatureBytes];
            if (!Base64Url.IsValid(token, out int length) || length != bytes.Length || !Base64Url.TryDecodeFromChars(token, bytes, out _))
            {
                return false;
            }

            Span<byte> expected = stackalloc byte[SignatureBytes];
            HMACSHA256.HashData(_key, bytes.AsSpan(0, SignedBytes), expected);
            if (!CryptographicOperations.FixedTimeEquals(expected, bytes.AsSpan(SignedBytes)))
            {
                return false;
            }

            long expiresAt = BinaryPrimitives.ReadInt64BigEndian(bytes.AsSpan(2 * IdBytes, ExpiryBytes));
            if (_timeProvider.GetUtcNow().ToUnixTimeSeconds() >= expiresAt)
            {
                return false;
            }

            shelfId = new Guid(bytes.AsSpan(0, IdBytes));
            fileId = new Guid(bytes.AsSpan(IdBytes, IdBytes));
            return true;
        }
    }
}
