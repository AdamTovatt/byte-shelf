using ByteShelf.Server.ChangeLog.Models;
using EasyReasy.Database;

namespace ByteShelf.Server.Replication.Repositories
{
    /// <summary>On a replica taking a whole copy: the primary's entities as read so far, until they are applied.</summary>
    public interface IReplicationCopyRepository
    {
        Task ClearAsync(IDbSession? session = null);

        /// <summary>
        /// Adds a page of the copy: an entity already held keeps only the newer state, and one the page
        /// gives as removed (a null payload) is dropped.
        /// </summary>
        Task AddAsync(IReadOnlyList<ChangeLogEntry> entries, IDbSession session);

        /// <summary>A page of the copied entries after <paramref name="afterSequence"/>, read in the caller's transaction.</summary>
        Task<ChangeLogPage> GetPageAsync(long afterSequence, ChangeLogPageLimit limit, IDbSession session);

        /// <summary>
        /// The ids in <paramref name="table"/> of the entities of <paramref name="entityType"/> that the copy
        /// does not hold; given an <paramref name="owner"/>, only those whose owner it does.
        /// </summary>
        Task<IReadOnlyList<Guid>> GetIdsMissingAsync(string entityType, string table, EntityOwner? owner, IDbSession session);

        /// <summary>
        /// The ids of the rows whose <paramref name="owner"/>, a row of <paramref name="ownerTable"/>, is not
        /// here, in a transaction that deferred the foreign keys.
        /// </summary>
        Task<IReadOnlyList<Guid>> GetIdsWithoutOwnerAsync(EntityOwner owner, string ownerTable, IDbSession session);
    }
}
