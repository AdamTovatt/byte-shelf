using ByteShelfClient;
using ByteShelfCommon;

namespace ByteShelf.Tools
{
    /// <summary>
    /// Where a file's contents are read from: the shelf as it is, or one deletion in its trash, which
    /// gives back a file without restoring it. Read by the file's id, so it is the file whose record
    /// the caller holds.
    /// </summary>
    internal sealed class FileSource
    {
        private readonly Func<ShelfFile, CancellationToken, Task<Stream>> _open;

        private FileSource(Func<ShelfFile, CancellationToken, Task<Stream>> open)
        {
            _open = open;
        }

        /// <summary>The shelf's live files.</summary>
        public static FileSource Live(IShelfClient shelf)
        {
            return new FileSource((file, cancellationToken) => shelf.OpenReadAsync(file.Id, cancellationToken));
        }

        /// <summary>The files deletion <paramref name="deletionId"/> removed from the shelf.</summary>
        public static FileSource Deleted(IShelfClient shelf, Guid deletionId)
        {
            return new FileSource((file, cancellationToken) => shelf.OpenDeletedReadAsync(deletionId, file.Id, cancellationToken));
        }

        public Task<Stream> OpenAsync(ShelfFile file, CancellationToken cancellationToken)
        {
            return _open(file, cancellationToken);
        }
    }
}
