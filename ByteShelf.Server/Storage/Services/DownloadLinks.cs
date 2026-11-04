using ByteShelf.Server.Shared;
using ByteShelf.Server.Shared.Auth;
using ByteShelfCommon;

namespace ByteShelf.Server.Storage.Services
{
    /// <summary>
    /// Signed links to one file's contents: for a browser that cannot send a token with a plain link
    /// or an image's source, and to share a file with someone who has no access. A link carries the
    /// shelf's and the file's ids, signed by <see cref="LinkTokens"/>; whoever holds it may read that
    /// file until it runs out, even if the access it was made with is taken away meanwhile.
    /// </summary>
    public sealed class DownloadLinks
    {
        /// <summary>The route links point at, served by <c>DownloadsController</c>; a link is this, a slash and its token.</summary>
        public const string Route = "api/downloads";

        // The signing key is derived from this: changing it ends every download link made before.
        private const string Purpose = "download";
        private const int IdBytes = 16;

        private readonly LinkTokens _tokens;
        private readonly PublicAddress _publicAddress;

        public DownloadLinks(LinkTokens tokens, PublicAddress publicAddress)
        {
            _tokens = tokens;
            _publicAddress = publicAddress;
        }

        /// <summary>
        /// A link to the file that works for <paramref name="lifetime"/>, or for
        /// <c>DownloadLink.Lifetime.Default</c> when null, made by <see cref="PublicAddress.LinkTo"/>.
        /// </summary>
        /// <exception cref="ArgumentException">The lifetime is one <c>DownloadLink.Lifetime.Normalize</c> refuses.</exception>
        /// <remarks>
        /// Ids, not a file: whether a file may be linked to is <see cref="ShelfEncryption.RequireDownloadLinkTarget"/>'s
        /// to say, as the link is made and again as it is used, since the file may be encrypted in between.
        /// </remarks>
        public DownloadLink Create(Guid shelfId, Guid fileId, TimeSpan? lifetime)
        {
            byte[] payload = new byte[2 * IdBytes];
            shelfId.TryWriteBytes(payload.AsSpan(0, IdBytes));
            fileId.TryWriteBytes(payload.AsSpan(IdBytes, IdBytes));

            (string token, DateTimeOffset expiresAt) = _tokens.Create(Purpose, payload, DownloadLink.Lifetime.Normalize(lifetime));
            return new DownloadLink { Url = _publicAddress.LinkTo($"/{Route}/{token}"), ExpiresAt = expiresAt };
        }

        /// <summary>The shelf and file a link's token names, when it is one of these links and has not run out.</summary>
        public bool TryRead(string token, out Guid shelfId, out Guid fileId)
        {
            shelfId = Guid.Empty;
            fileId = Guid.Empty;

            if (!_tokens.TryRead(Purpose, token, out byte[]? payload) || payload.Length != 2 * IdBytes)
            {
                return false;
            }

            shelfId = new Guid(payload.AsSpan(0, IdBytes));
            fileId = new Guid(payload.AsSpan(IdBytes, IdBytes));
            return true;
        }
    }
}
