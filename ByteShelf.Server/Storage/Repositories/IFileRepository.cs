using ByteShelf.Server.Storage.Models;
using EasyReasy.Database;

namespace ByteShelf.Server.Storage.Repositories
{
    /// <summary>The <c>file</c> table. Every write logs the change in the caller's transaction.</summary>
    public interface IFileRepository
    {
        /// <summary>The file with <paramref name="id"/>, live or in the trash.</summary>
        Task<FileRecord?> GetByIdAsync(Guid id, IDbSession? session = null);

        /// <summary>The live file called <paramref name="name"/> directly inside <paramref name="directoryId"/>.</summary>
        Task<FileRecord?> GetLiveByNameAsync(Guid directoryId, string name, IDbSession? session = null);

        /// <summary>The live files directly inside <paramref name="directoryId"/>, by name.</summary>
        Task<IReadOnlyList<FileRecord>> GetLiveInDirectoryAsync(Guid directoryId, IDbSession? session = null);

        Task InsertAsync(FileRecord file, IDbSession session);

        /// <summary>Moves a live file to the trash under <paramref name="deletionId"/>. False when it is not live.</summary>
        Task<bool> SoftDeleteAsync(Guid id, Guid deletionId, DateTimeOffset deletedAt, IDbSession session);

        /// <summary>
        /// Moves every live file directly in one of <paramref name="directoryIds"/> to the trash under
        /// <paramref name="deletionId"/>; answers how many. The ids come from
        /// <see cref="IDirectoryRepository.SoftDeleteSubtreeAsync"/>.
        /// </summary>
        Task<int> SoftDeleteInDirectoriesAsync(IReadOnlyList<Guid> directoryIds, Guid deletionId, DateTimeOffset deletedAt, IDbSession session);

        /// <summary>Puts a live file in <paramref name="directoryId"/> as <paramref name="name"/>; null when it is not live.</summary>
        Task<FileRecord?> MoveAsync(Guid id, Guid directoryId, string name, DateTimeOffset movedAt, IDbSession session);

        /// <summary>Brings back every file of a deletion; answers how many.</summary>
        Task<int> RestoreDeletionAsync(Guid deletionId, DateTimeOffset restoredAt, IDbSession session);

        /// <summary>
        /// Files deleted before <paramref name="deletedBefore"/>, on the shelf <paramref name="shelfId"/> or
        /// any shelf when it is null, oldest first, at most <paramref name="limit"/>.
        /// </summary>
        Task<IReadOnlyList<FileRecord>> GetTrashedBeforeAsync(DateTimeOffset deletedBefore, Guid? shelfId, int limit, IDbSession? session = null);

        /// <summary>Removes a trashed file's row for good. False when it is not in the trash.</summary>
        Task<bool> PurgeAsync(Guid id, DateTimeOffset purgedAt, IDbSession session);

        /// <summary>Whether any row, live or trashed, points at the object <paramref name="sha256"/>.</summary>
        Task<bool> IsObjectReferencedAsync(string sha256, IDbSession? session = null);

        /// <summary>
        /// Writes a file as a primary's change log holds it, logged as <paramref name="action"/>. A
        /// different live file at the same place is moved to the trash first; the log brings it back
        /// where it belongs if it still exists.
        /// </summary>
        Task UpsertReplicatedAsync(FileRecord file, string action, DateTimeOffset at, IDbSession session);

        /// <summary>Removes every file of a shelf, trash included, for removing the shelf.</summary>
        Task PurgeForShelfAsync(Guid shelfId, DateTimeOffset purgedAt, IDbSession session);
    }
}
