using ByteShelf.Server.Storage.Models;
using ByteShelfCommon;

namespace ByteShelf.Server.Storage.Services
{
    /// <summary>Listing, creating, moving and deleting directories.</summary>
    public interface IDirectoryService
    {
        /// <summary>The live directories and files directly inside a directory.</summary>
        /// <exception cref="Shared.Errors.NotFoundException">There is no such live directory.</exception>
        Task<DirectoryListing> ListAsync(Shelf shelf, EntryReference reference);

        /// <summary>One page of the live directories and files under a directory, by path, as <paramref name="query"/> asks.</summary>
        /// <exception cref="ArgumentException">The query is not allowed.</exception>
        /// <exception cref="Shared.Errors.NotFoundException">There is no such live directory.</exception>
        Task<DirectoryTree> ListTreeAsync(Shelf shelf, TreeQuery query);

        /// <summary>
        /// The directory at <paramref name="path"/>, creating it and any missing directory above it.
        /// An existing directory is answered as it is.
        /// </summary>
        /// <exception cref="ArgumentException">The path is not allowed.</exception>
        /// <exception cref="Shared.Errors.ConflictException">A file is in the way.</exception>
        Task<ShelfDirectory> CreateAsync(Shelf shelf, string path);

        /// <summary>
        /// Moves or renames a directory with everything in it, creating any missing directory above the
        /// destination. Of what was there, only the directory's own record changes.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// The directory is the root; the destination is the root, inside the directory, or not an allowed
        /// path; or a path under the directory would grow past <see cref="ShelfPath.MaxPathLength"/>.
        /// </exception>
        /// <exception cref="Shared.Errors.NotFoundException">There is no such live directory.</exception>
        /// <exception cref="Shared.Errors.ConflictException">The destination is taken, or a file is in the way.</exception>
        Task<ShelfDirectory> MoveAsync(Shelf shelf, EntryReference reference, string toPath);

        /// <summary>Moves a directory and everything in it to the trash as one deletion.</summary>
        /// <exception cref="ArgumentException">The directory is the root.</exception>
        /// <exception cref="Shared.Errors.NotFoundException">There is no such live directory.</exception>
        Task<TrashEntry> DeleteAsync(Shelf shelf, EntryReference reference);
    }
}
