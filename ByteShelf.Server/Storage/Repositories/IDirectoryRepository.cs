using ByteShelf.Server.Storage.Models;
using EasyReasy.Database;

namespace ByteShelf.Server.Storage.Repositories
{
    /// <summary>The <c>directory</c> table. Every write logs the change in the caller's transaction.</summary>
    public interface IDirectoryRepository
    {
        /// <summary>The directory with <paramref name="id"/>, live or in the trash.</summary>
        Task<DirectoryRecord?> GetByIdAsync(Guid id, IDbSession? session = null);

        Task<DirectoryRecord> GetRootAsync(Guid shelfId, IDbSession? session = null);

        /// <summary>The live directory called <paramref name="name"/> directly inside <paramref name="parentId"/>.</summary>
        Task<DirectoryRecord?> GetLiveChildAsync(Guid parentId, string name, IDbSession? session = null);

        /// <summary>The live directories directly inside <paramref name="parentId"/>, by name.</summary>
        Task<IReadOnlyList<DirectoryRecord>> GetLiveChildrenAsync(Guid parentId, IDbSession? session = null);

        /// <summary>
        /// The names from the shelf's root down to <paramref name="id"/>, the root's empty name excluded:
        /// empty for the root itself.
        /// </summary>
        Task<IReadOnlyList<string>> GetPathNamesAsync(Guid id, IDbSession? session = null);

        Task InsertAsync(DirectoryRecord directory, IDbSession session);

        /// <summary>Brings back every directory of a deletion; answers how many.</summary>
        Task<int> RestoreDeletionAsync(Guid deletionId, DateTimeOffset restoredAt, IDbSession session);

        /// <summary>
        /// Directories deleted before <paramref name="deletedBefore"/> that no row, live or trashed, still
        /// sits in, so they can be purged without leaving anything orphaned.
        /// </summary>
        Task<IReadOnlyList<DirectoryRecord>> GetPurgeableBeforeAsync(DateTimeOffset deletedBefore, int limit, IDbSession? session = null);

        /// <summary>Removes a trashed directory's row for good. False when it is not in the trash.</summary>
        Task<bool> PurgeAsync(Guid id, DateTimeOffset purgedAt, IDbSession session);
    }
}
