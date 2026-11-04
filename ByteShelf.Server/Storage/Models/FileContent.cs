using ByteShelfCommon;

namespace ByteShelf.Server.Storage.Models
{
    /// <summary>A live file and its contents, already open, for streaming a download.</summary>
    public sealed class FileContent : IAsyncDisposable
    {
        public required ShelfFile File { get; init; }

        /// <summary>
        /// The object's bytes, opened as the file was looked up: a swap that removes them afterwards
        /// leaves this read whole. Whoever sends it disposes it.
        /// </summary>
        public required FileStream Contents { get; init; }

        public ValueTask DisposeAsync()
        {
            return Contents.DisposeAsync();
        }
    }
}
