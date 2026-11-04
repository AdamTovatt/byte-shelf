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

        /// <summary>The copied entries after <paramref name="afterSequence"/>, oldest first.</summary>
        Task<IReadOnlyList<ChangeLogEntry>> GetAfterAsync(long afterSequence, int limit, IDbSession? session = null);

        /// <summary>The ids in <paramref name="table"/> of the entities of <paramref name="entityType"/> that the copy does not hold.</summary>
        Task<IReadOnlyList<Guid>> GetIdsMissingAsync(string entityType, string table, IDbSession session);
    }
}
