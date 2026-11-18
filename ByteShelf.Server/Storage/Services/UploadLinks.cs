using ByteShelf.Server.Access.Models;
using ByteShelf.Server.Shared;
using ByteShelf.Server.Shared.Auth;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Repositories;
using ByteShelfCommon;

namespace ByteShelf.Server.Storage.Services
{
    /// <summary>
    /// Links to store one file without signing in. The link's row holds its <see cref="UploadLinkTarget"/>;
    /// the maker's access is checked again when it is used (<see cref="LinkAccess"/>).
    /// </summary>
    public sealed class UploadLinks : LinkStore<UploadLinkTarget>
    {
        /// <summary>The route links point at; a link is this, a slash and its token.</summary>
        public const string Route = "api/upload-links";

        public UploadLinks(LinkTokens tokens, ILinkRepository linkRepository, PublicAddress publicAddress, TimeProvider timeProvider)
            : base(tokens, linkRepository, publicAddress, timeProvider, ShelfLinkKind.Upload, Route, purpose: "upload-link")
        {
        }

        /// <summary>A link made by <paramref name="access"/>'s caller to store a file as <paramref name="request"/> asks.</summary>
        public async Task<UploadLink> CreateAsync(ShelfAccess access, NormalizedUploadLinkRequest request)
        {
            (LinkRecord link, string url) = await CreateAsync(access, new UploadLinkTarget(request.Path, request.Overwrite, request.Description), request.Lifetime);
            return new UploadLink { Id = link.Id, Url = url, ExpiresAt = link.ExpiresAt, Path = request.Path, Overwrite = request.Overwrite };
        }
    }
}
