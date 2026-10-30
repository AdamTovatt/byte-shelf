using ByteShelf.Server.Storage.Models;
using ByteShelfCommon;

namespace ByteShelf.Server.Storage.Services
{
    /// <summary>Reading what is on a shelf, and moving files to the trash.</summary>
    public interface IFileService
    {
        Task<ShelfFile> GetFileAsync(Shelf shelf, EntryReference reference);

        Task<FileContent> GetContentAsync(Shelf shelf, EntryReference reference);

        Task<DirectoryListing> ListDirectoryAsync(Shelf shelf, EntryReference reference);

        /// <summary>Moves a live file to the trash; answers the deletion, which can be restored until it is purged.</summary>
        Task<TrashEntry> DeleteFileAsync(Shelf shelf, EntryReference reference);
    }
}
