using ByteShelf.Server.Storage.Models;
using ByteShelfCommon;
using EasyReasy.Database;

namespace ByteShelf.Server.Storage.Services
{
    /// <summary>
    /// The trash: what deletions removed, restoring them within the restore window, and purging them
    /// for good once it has passed.
    /// </summary>
    public interface ITrashService
    {
        /// <summary>Moves a live file to the trash as a deletion of its own, inside the caller's transaction.</summary>
        Task<Guid> TrashFileAsync(Guid fileId, DateTimeOffset at, IDbSession session);

        /// <summary>Every deletion on the shelf still inside the restore window, newest first.</summary>
        Task<IReadOnlyList<TrashEntry>> GetTrashAsync(Shelf shelf);

        /// <exception cref="Shared.Errors.NotFoundException">The deletion is not on the shelf, or its window has passed.</exception>
        Task<TrashEntry> GetEntryAsync(Shelf shelf, Guid deletionId);

        /// <summary>Brings back what one deletion removed, to where it was, while its window lasts.</summary>
        Task RestoreAsync(Shelf shelf, Guid deletionId);

        /// <summary>
        /// Removes every row deleted longer ago than the restore window, then every object those rows
        /// pointed at that nothing else does. Answers how many files were purged.
        /// </summary>
        Task<int> PurgeExpiredAsync(CancellationToken cancellationToken);
    }
}
