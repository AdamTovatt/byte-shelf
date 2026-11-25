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
        private readonly Func<ShelfFile, DerivedKind, CancellationToken, Task<DerivedFileContent>> _openDerived;

        private FileSource(
            Func<ShelfFile, CancellationToken, Task<Stream>> open,
            Func<ShelfFile, DerivedKind, CancellationToken, Task<DerivedFileContent>> openDerived)
        {
            _open = open;
            _openDerived = openDerived;
        }

        /// <summary>The shelf's live files.</summary>
        public static FileSource Live(IShelfClient shelf)
        {
            return new FileSource(
                (file, cancellationToken) => shelf.OpenReadAsync(file.Id, cancellationToken),
                (file, kind, cancellationToken) => shelf.OpenDerivedAsync(file.Id, kind, cancellationToken));
        }

        /// <summary>The files deletion <paramref name="deletionId"/> removed from the shelf.</summary>
        public static FileSource Deleted(IShelfClient shelf, Guid deletionId)
        {
            return new FileSource(
                (file, cancellationToken) => shelf.OpenDeletedReadAsync(deletionId, file.Id, cancellationToken),
                (file, kind, cancellationToken) => shelf.OpenDeletedDerivedAsync(deletionId, file.Id, kind, cancellationToken));
        }

        public Task<Stream> OpenAsync(ShelfFile file, CancellationToken cancellationToken)
        {
            return _open(file, cancellationToken);
        }

        /// <summary>What a plugin of the server's made from the file's contents (<see cref="IShelfClient.OpenDerivedAsync"/>).</summary>
        public Task<DerivedFileContent> OpenDerivedAsync(ShelfFile file, DerivedKind kind, CancellationToken cancellationToken)
        {
            return _openDerived(file, kind, cancellationToken);
        }
    }
}
