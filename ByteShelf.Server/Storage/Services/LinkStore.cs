using ByteShelf.Server.Access.Models;
using ByteShelf.Server.Shared;
using ByteShelf.Server.Shared.Auth;
using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Repositories;

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
        private readonly LinkAccess _linkAccess;
        private readonly string _route;
        private readonly string _purpose;

        /// <param name="route">The route the kind's links point at; a link is this, a slash and its token.</param>
        /// <param name="purpose">What the kind's signing key is derived from: changing it ends every link of the kind made before.</param>
        private protected LinkStore(LinkTokens tokens, ILinkRepository linkRepository, PublicAddress publicAddress, TimeProvider timeProvider, LinkAccess linkAccess, string route, string purpose)
        {
            _tokens = tokens;
            _linkRepository = linkRepository;
            _publicAddress = publicAddress;
            _timeProvider = timeProvider;
            _linkAccess = linkAccess;
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

            // Each kind signs with its own purpose, so a row of another kind is never named; the match on
            // the target's type is what guarantees it.
            LinkRecord? link = await _linkRepository.GetLiveAsync(linkId, _timeProvider.GetUtcNow());
            return link is { Target: TTarget target } ? new FoundLink<TTarget>(link, target) : null;
        }

        /// <summary>
        /// What a public request through a link works with: the link's target, and its maker's access as
        /// it is now, once the maker may still do what the link does (<see cref="LinkAccess.ResolveAsync"/>).
        /// </summary>
        /// <exception cref="NotFoundException">
        /// No such link, it ran out or was revoked, or its maker has no access to the shelf any more;
        /// <paramref name="notValidMessage"/> in every case, which tells the holder no more than that.
        /// </exception>
        public async Task<(ShelfAccess Access, TTarget Target)> OpenAsync(string token, string notValidMessage)
        {
            FoundLink<TTarget> found = await FindAsync(token) ?? throw new NotFoundException(notValidMessage);
            return (await _linkAccess.ResolveAsync(found.Link, notValidMessage), found.Target);
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
