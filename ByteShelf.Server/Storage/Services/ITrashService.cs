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

        /// <summary>
        /// Moves a live directory and everything live under it to the trash as one deletion, inside the
        /// caller's transaction.
        /// </summary>
        Task<Guid> TrashDirectoryAsync(Guid directoryId, DateTimeOffset at, IDbSession session);

        /// <summary>Every deletion on the shelf still inside the restore window, newest first.</summary>
        Task<IReadOnlyList<TrashEntry>> GetTrashAsync(Shelf shelf);

        /// <exception cref="Shared.Errors.NotFoundException">The deletion is not on the shelf, or its window has passed.</exception>
        Task<TrashEntry> GetEntryAsync(Shelf shelf, Guid deletionId);

        /// <summary>
        /// One page of what a deletion removed, by the paths it had, with what the deletion was and the
        /// encrypted folder its files were in (<see cref="DeletionTree"/>). <see cref="TreeQuery.Under"/>
        /// keeps what it took at that path and under it, the root keeping all of it; <see cref="TreeQuery.Depth"/>
        /// is not read. A read, so a replica answers it too.
        /// </summary>
        /// <exception cref="Shared.Errors.NotFoundException">
        /// The deletion is not on the shelf, its window has passed, or it took nothing at that path.
        /// </exception>
        Task<DeletionTree> ListDeletionAsync(Shelf shelf, Guid deletionId, TreeQuery page);

        /// <summary>The contents of a file deletion <paramref name="deletionId"/> removed, with the record it had.</summary>
        /// <exception cref="Shared.Errors.NotFoundException">The deletion or the file is not in the shelf's trash, or has been purged.</exception>
        Task<FileContent> GetDeletedContentAsync(Shelf shelf, Guid deletionId, Guid fileId);

        /// <summary>Brings back what one deletion removed, to where it was, while its window lasts.</summary>
        Task RestoreAsync(Shelf shelf, Guid deletionId);

        /// <summary>
        /// Removes every row deleted longer ago than the restore window, then every object those rows
        /// pointed at that nothing else does. Answers how many files were purged.
        /// </summary>
        Task<int> PurgeExpiredAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Removes everything in the shelf's trash for good, whatever is left of its window, and every
        /// object only it pointed at. First, in one transaction, it puts all of it past its window, so none
        /// of it can be restored from then on; then it purges in batches, and whatever a crash or
        /// cancellation leaves is purged by maintenance's next pass. A deletion made while it purges is
        /// kept. Answers how many files were purged.
        /// </summary>
        Task<int> EmptyAsync(Shelf shelf, CancellationToken cancellationToken);
    }
}
