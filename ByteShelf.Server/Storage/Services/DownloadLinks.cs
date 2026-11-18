using ByteShelf.Server.Access.Models;
using ByteShelf.Server.Shared;
using ByteShelf.Server.Shared.Auth;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Repositories;
using ByteShelfCommon;

namespace ByteShelf.Server.Storage.Services
{
    /// <summary>
    /// Links to one file's contents: to share a file with someone who has no access. The link's row
    /// names the file by id (<see cref="DownloadLinkTarget"/>). Whoever holds it may read the file until
    /// it runs out or is revoked, while its maker may still read the shelf (<see cref="LinkAccess"/>).
    /// </summary>
    public sealed class DownloadLinks : LinkStore<DownloadLinkTarget>
    {
        /// <summary>The route links point at, served by <c>DownloadsController</c>; a link is this, a slash and its token.</summary>
        public const string Route = "api/downloads";

        public DownloadLinks(LinkTokens tokens, ILinkRepository linkRepository, PublicAddress publicAddress, TimeProvider timeProvider)
            : base(tokens, linkRepository, publicAddress, timeProvider, ShelfLinkKind.Download, Route, purpose: "download-link")
        {
        }

        /// <summary>
        /// A link to the file made by <paramref name="access"/>'s caller, which works for
        /// <paramref name="lifetime"/>, or for <c>DownloadLink.Lifetime.Default</c> when null.
        /// </summary>
        /// <exception cref="ArgumentException">The lifetime is one <c>DownloadLink.Lifetime.Normalize</c> refuses.</exception>
        /// <remarks>
        /// An id, not a file: whether a file may be linked to is <see cref="ShelfEncryption.RequireDownloadLinkTarget"/>'s
        /// to say, as the link is made and again as it is used, since the file may be encrypted in between.
        /// </remarks>
        public async Task<DownloadLink> CreateAsync(ShelfAccess access, Guid fileId, TimeSpan? lifetime)
        {
            (LinkRecord link, string url) = await CreateAsync(access, new DownloadLinkTarget(fileId), DownloadLink.Lifetime.Normalize(lifetime));
            return new DownloadLink { Id = link.Id, Url = url, ExpiresAt = link.ExpiresAt };
        }
    }
}
