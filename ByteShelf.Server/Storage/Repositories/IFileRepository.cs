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

        /// <summary>Brings back every file of a deletion; answers how many.</summary>
        Task<int> RestoreDeletionAsync(Guid deletionId, DateTimeOffset restoredAt, IDbSession session);

        /// <summary>Files deleted before <paramref name="deletedBefore"/>, oldest first, at most <paramref name="limit"/>.</summary>
        Task<IReadOnlyList<FileRecord>> GetTrashedBeforeAsync(DateTimeOffset deletedBefore, int limit, IDbSession? session = null);

        /// <summary>Removes a trashed file's row for good. False when it is not in the trash.</summary>
        Task<bool> PurgeAsync(Guid id, DateTimeOffset purgedAt, IDbSession session);

        /// <summary>Whether any row, live or trashed, points at the object <paramref name="sha256"/>.</summary>
        Task<bool> IsObjectReferencedAsync(string sha256, IDbSession? session = null);
    }
}
