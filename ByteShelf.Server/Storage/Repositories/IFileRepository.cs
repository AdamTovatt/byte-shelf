using ByteShelf.Server.Storage.Models;
using ByteShelfCommon;
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

        /// <summary>Sets a live file's description, null for none; null when the file is not live.</summary>
        Task<FileRecord?> SetDescriptionAsync(Guid id, string? description, DateTimeOffset at, IDbSession session);

        /// <summary>
        /// The live files under <paramref name="directoryId"/>, at any depth, that match the criteria of
        /// <paramref name="search"/> (normalized), by full path, each with that path; at most
        /// <paramref name="maxRows"/> of them, which the caller sets one past the page so it can tell
        /// whether another follows. <paramref name="directoryPath"/> is the directory's own path.
        /// </summary>
        Task<IReadOnlyList<(FileRecord File, string Path)>> SearchAsync(Guid directoryId, string directoryPath, FileSearch search, int maxRows, IDbSession? session = null);

        /// <summary>
        /// The live files under <paramref name="directoryId"/>, down to <paramref name="maxDepth"/> levels
        /// when set (1: only those directly in it), whose full path comes after <paramref name="after"/>;
        /// by full path, each with that path, at most <paramref name="maxRows"/> of them.
        /// <paramref name="directoryPath"/> is the directory's own path.
        /// </summary>
        Task<IReadOnlyList<(FileRecord File, string Path)>> GetLiveSubtreeAsync(Guid directoryId, string directoryPath, int? maxDepth, string? after, int maxRows, IDbSession? session = null);

        /// <summary>Brings back every file of a deletion; answers how many.</summary>
        Task<int> RestoreDeletionAsync(Guid deletionId, DateTimeOffset restoredAt, IDbSession session);

        /// <summary>
        /// Gives every file in the shelf's trash the deletion time <paramref name="deletedAt"/>, one past
        /// any restore window, so none of them is listed or restored again and the purge of expired rows
        /// takes them; answers how many. Not logged, unlike every other write: the log already holds them
        /// as deleted, and their purge is logged. A replica keeps what it follows in its own trash for its
        /// own window, which this must not cut short.
        /// </summary>
        Task<int> ExpireTrashAsync(Guid shelfId, DateTimeOffset deletedAt, IDbSession session);

        /// <summary>
        /// Files deleted before <paramref name="deletedBefore"/>, on the shelf <paramref name="shelfId"/> or
        /// any shelf when it is null, oldest first, at most <paramref name="limit"/>.
        /// </summary>
        Task<IReadOnlyList<FileRecord>> GetTrashedBeforeAsync(DateTimeOffset deletedBefore, Guid? shelfId, int limit, IDbSession? session = null);

        /// <summary>Removes a trashed file's row for good. False when it is not in the trash.</summary>
        Task<bool> PurgeAsync(Guid id, DateTimeOffset purgedAt, IDbSession session);

        /// <summary>Whether any row, live or trashed, points at the object <paramref name="sha256"/>.</summary>
        Task<bool> IsObjectReferencedAsync(string sha256, IDbSession? session = null);

        /// <summary>The SHA-256 of every object a row points at, live or trashed, each once, in order.</summary>
        Task<IReadOnlyList<string>> GetReferencedObjectsAsync(IDbSession? session = null);

        /// <summary>
        /// Removes every row, live or trashed, pointing at the object <paramref name="sha256"/>, each logged
        /// as purged; answers how many. For a backup whose object the server purged meanwhile.
        /// </summary>
        Task<int> PurgeAllWithObjectAsync(string sha256, DateTimeOffset purgedAt, IDbSession session);

        /// <summary>
        /// Writes a file as a primary's change log holds it, logged as <paramref name="action"/>. A
        /// different live file at the same place is moved to the trash first, as
        /// <paramref name="displacedDeletionId"/>; the log brings it back where it belongs if it still exists.
        /// </summary>
        Task UpsertReplicatedAsync(FileRecord file, string action, Guid displacedDeletionId, DateTimeOffset at, IDbSession session);

        /// <summary>Removes every file of a shelf, trash included, for removing the shelf.</summary>
        Task PurgeForShelfAsync(Guid shelfId, DateTimeOffset purgedAt, IDbSession session);
    }
}
