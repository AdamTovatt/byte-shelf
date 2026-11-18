using ByteShelf.Server.Access.Models;
using ByteShelf.Server.Shared;
using ByteShelf.Server.Shared.Auth;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Repositories;
using ByteShelfCommon;

namespace ByteShelf.Server.Storage.Services
{
    /// <summary>A link of one kind as its token found it: its row, and its target as that kind's.</summary>
    public sealed record FoundLink<TTarget>(LinkRecord Link, TTarget Target)
        where TTarget : LinkTarget;

    /// <summary>
    /// What every kind of link shares: a link is a <c>shelf_link</c> row holding its target and who made
    /// it, and a token signed by <see cref="LinkTokens"/> for the kind's purpose naming the row. Each
    /// kind (<see cref="DownloadLinks"/>, <see cref="UploadLinks"/>) adds what it answers when made.
    /// </summary>
    /// <remarks>
    /// Making a link needs only access to the shelf, and nothing caps how many a caller makes: a row
    /// lives at most as long as its kind's longest lifetime before maintenance removes it.
    /// </remarks>
    public abstract class LinkStore<TTarget>
        where TTarget : LinkTarget
    {
        private readonly LinkTokens _tokens;
        private readonly ILinkRepository _linkRepository;
        private readonly PublicAddress _publicAddress;
        private readonly TimeProvider _timeProvider;
        private readonly ShelfLinkKind _kind;
        private readonly string _route;
        private readonly string _purpose;

        /// <param name="route">The route the kind's links point at; a link is this, a slash and its token.</param>
        /// <param name="purpose">What the kind's signing key is derived from: changing it ends every link of the kind made before.</param>
        private protected LinkStore(LinkTokens tokens, ILinkRepository linkRepository, PublicAddress publicAddress, TimeProvider timeProvider, ShelfLinkKind kind, string route, string purpose)
        {
            _tokens = tokens;
            _linkRepository = linkRepository;
            _publicAddress = publicAddress;
            _timeProvider = timeProvider;
            _kind = kind;
            _route = route;
            _purpose = purpose;
        }

        /// <summary>The link a token names, when it is one of this kind and has neither run out nor been revoked.</summary>
        public async Task<FoundLink<TTarget>?> FindAsync(string token)
        {
            if (!_tokens.TryRead(_purpose, token, out Guid linkId))
            {
                return null;
            }

            // The row is read by kind, so its target is this kind's.
            LinkRecord? link = await _linkRepository.GetLiveAsync(linkId, _kind, _timeProvider.GetUtcNow());
            return link is { Target: TTarget target } ? new FoundLink<TTarget>(link, target) : null;
        }

        /// <summary>
        /// Stores a link to <paramref name="target"/> made by <paramref name="access"/>'s caller, working
        /// for <paramref name="lifetime"/>, and answers its row and its URL, made by <see cref="PublicAddress.LinkTo"/>.
        /// </summary>
        private protected async Task<(LinkRecord Link, string Url)> CreateAsync(ShelfAccess access, TTarget target, TimeSpan lifetime)
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();
            LinkRecord link = new LinkRecord
            {
                Id = Guid.NewGuid(),
                ShelfId = access.Shelf.Id,
                Target = target,
                CreatedBy = access.Caller.Reference,
                CreatedAt = now,
                ExpiresAt = now + lifetime,
            };

            await _linkRepository.InsertAsync(link);
            return (link, _publicAddress.LinkTo($"/{_route}/{_tokens.Create(_purpose, link.Id)}"));
        }
    }
}
