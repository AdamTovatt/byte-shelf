using ByteShelfCommon;

namespace ByteShelf.Server.Storage.Models
{
    /// <summary>A live file and where its contents are on disk, for streaming a download.</summary>
    public class FileContent
    {
        public required ShelfFile File { get; init; }

        /// <summary>The object's path under <c>objects/</c>.</summary>
        public required string ObjectPath { get; init; }
    }
}
