using ByteShelf.Server.Storage.Models;
using ByteShelfCommon;

namespace ByteShelf.Server.Storage.Services
{
    /// <summary>Reading, moving and deleting files.</summary>
    public interface IFileService
    {
        Task<ShelfFile> GetFileAsync(Shelf shelf, EntryReference reference);

        Task<FileContent> GetContentAsync(Shelf shelf, EntryReference reference);

        /// <summary>Moves a live file to the trash; answers the deletion, which can be restored until it is purged.</summary>
        Task<TrashEntry> DeleteFileAsync(Shelf shelf, EntryReference reference);

        /// <summary>
        /// Moves or renames a file, creating any missing directory above the destination. The stored
        /// contents stay where they are.
        /// </summary>
        /// <exception cref="ArgumentException">The destination is the root or not an allowed path.</exception>
        /// <exception cref="Shared.Errors.NotFoundException">There is no such live file.</exception>
        /// <exception cref="Shared.Errors.ConflictException">The destination is taken, or a file is in the way.</exception>
        Task<ShelfFile> MoveFileAsync(Shelf shelf, EntryReference reference, string toPath);

        /// <summary>Sets a file's description; null or blank removes it.</summary>
        /// <exception cref="ArgumentException">It is longer than <see cref="FileDescription.MaxLength"/>.</exception>
        /// <exception cref="Shared.Errors.NotFoundException">There is no such live file.</exception>
        Task<ShelfFile> SetDescriptionAsync(Shelf shelf, EntryReference reference, string? description);

        /// <summary>One page of the live files that match <paramref name="search"/>, by path.</summary>
        /// <exception cref="ArgumentException">A criterion is not allowed.</exception>
        /// <exception cref="Shared.Errors.NotFoundException">There is no directory at <see cref="FileSearch.Under"/>.</exception>
        Task<FileSearchResults> SearchAsync(Shelf shelf, FileSearch search);
    }
}
