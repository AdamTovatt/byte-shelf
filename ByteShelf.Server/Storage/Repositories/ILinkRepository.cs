using ByteShelf.Server.Access.Models;
using ByteShelf.Server.Storage.Models;
using ByteShelfCommon;
using EasyReasy.Database;

namespace ByteShelf.Server.Storage.Repositories
{
    /// <summary>
    /// The <c>shelf_link</c> table: this server's own links, which the change log leaves out. A link past
    /// its expiry is never answered, though its row stays until <see cref="DeleteExpiredAsync"/>.
    /// </summary>
    public interface ILinkRepository
    {
        Task InsertAsync(LinkRecord link, IDbSession? session = null);

        /// <summary>The link of <paramref name="kind"/> with <paramref name="id"/>, unless it has run out by <paramref name="now"/> or is gone.</summary>
        Task<LinkRecord?> GetLiveAsync(Guid id, ShelfLinkKind kind, DateTimeOffset now, IDbSession? session = null);

        /// <summary>
        /// The shelf's links that have not run out by <paramref name="now"/>, newest first: every one, or
        /// only those <paramref name="madeBy"/> made when it is given.
        /// </summary>
        Task<IReadOnlyList<ListedLinkRecord>> GetLiveForShelfAsync(Guid shelfId, ShelfCallerReference? madeBy, DateTimeOffset now, IDbSession? session = null);

        /// <summary>
        /// Removes one of the shelf's links, which then stops working: any, or only one
        /// <paramref name="madeBy"/> made when it is given. False when there is no such link.
        /// </summary>
        Task<bool> DeleteAsync(Guid shelfId, Guid id, ShelfCallerReference? madeBy, IDbSession? session = null);

        /// <summary>Removes the links that have run out by <paramref name="now"/>; answers how many.</summary>
        Task<int> DeleteExpiredAsync(DateTimeOffset now, IDbSession? session = null);
    }
}
