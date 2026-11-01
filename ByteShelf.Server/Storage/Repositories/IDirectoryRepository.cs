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
        /// The live directories under <paramref name="directoryId"/>, down to <paramref name="maxDepth"/>
        /// levels when set (1: only those directly in it), whose full path comes after
        /// <paramref name="after"/>; by full path, each with that path, at most <paramref name="maxRows"/>
        /// of them. <paramref name="directoryPath"/> is the directory's own path.
        /// </summary>
        Task<IReadOnlyList<(DirectoryRecord Directory, string Path)>> GetLiveSubtreeAsync(Guid directoryId, string directoryPath, int? maxDepth, string? after, int maxRows, IDbSession? session = null);

        /// <summary>
        /// The names from the shelf's root down to <paramref name="id"/>, the root's empty name excluded:
        /// empty for the root itself.
        /// </summary>
        Task<IReadOnlyList<string>> GetPathNamesAsync(Guid id, IDbSession? session = null);

        Task InsertAsync(DirectoryRecord directory, IDbSession session);

        /// <summary>
        /// The paths of every directory and file under <paramref name="id"/>, live or in the trash and
        /// the directory itself included (as the empty path), relative to it (<c>/a/b.txt</c>), whose UTF-8
        /// form is longer than <paramref name="utf8Bytes"/> bytes. A path never has more UTF-16 characters
        /// than UTF-8 bytes, so every path longer than that many characters is among them; measure each.
        /// </summary>
        Task<IReadOnlyList<string>> GetRelativePathsLongerThanAsync(Guid id, int utf8Bytes, IDbSession? session = null);

        /// <summary>Puts a live directory under <paramref name="parentId"/> as <paramref name="name"/>; null when it is not live.</summary>
        Task<DirectoryRecord?> MoveAsync(Guid id, Guid parentId, string name, DateTimeOffset movedAt, IDbSession session);

        /// <summary>
        /// Moves a live directory and every live directory under it to the trash under one
        /// <paramref name="deletionId"/>; answers the ids it marked, empty when the directory is not
        /// live. Hand them to <see cref="IFileRepository.SoftDeleteInDirectoriesAsync"/> to trash the
        /// files in them with the same deletion.
        /// </summary>
        Task<IReadOnlyList<Guid>> SoftDeleteSubtreeAsync(Guid id, Guid deletionId, DateTimeOffset deletedAt, IDbSession session);

        /// <summary>Removes a shelf's root directory row, for deleting an empty shelf.</summary>
        Task DeleteRootAsync(Guid shelfId, DateTimeOffset deletedAt, IDbSession session);

        /// <summary>Brings back every directory of a deletion; answers how many.</summary>
        Task<int> RestoreDeletionAsync(Guid deletionId, DateTimeOffset restoredAt, IDbSession session);

        /// <summary>
        /// Directories deleted before <paramref name="deletedBefore"/>, on the shelf <paramref name="shelfId"/>
        /// or any shelf when it is null, that no row, live or trashed, still sits in, so they can be purged
        /// without leaving anything orphaned.
        /// </summary>
        Task<IReadOnlyList<DirectoryRecord>> GetPurgeableBeforeAsync(DateTimeOffset deletedBefore, Guid? shelfId, int limit, IDbSession? session = null);

        /// <summary>Removes a trashed directory's row for good. False when it is not in the trash.</summary>
        Task<bool> PurgeAsync(Guid id, DateTimeOffset purgedAt, IDbSession session);

        /// <summary>
        /// Writes a directory as a primary's change log holds it, logged as <paramref name="action"/>. A
        /// different live directory of the same name in the same parent is moved to the trash first; the
        /// log brings it back where it belongs if it still exists.
        /// </summary>
        Task UpsertReplicatedAsync(DirectoryRecord directory, string action, DateTimeOffset at, IDbSession session);

        /// <summary>
        /// Moves one directory (never a root) to the trash, unless it is there already, without what is
        /// under it, so it is purged only after the restore window. False when it was not live.
        /// </summary>
        Task<bool> TrashIfLiveAsync(Guid id, DateTimeOffset at, IDbSession session);

        /// <summary>Removes every directory of a shelf, its root and trash included, for removing the shelf.</summary>
        Task PurgeForShelfAsync(Guid shelfId, DateTimeOffset purgedAt, IDbSession session);
    }
}
