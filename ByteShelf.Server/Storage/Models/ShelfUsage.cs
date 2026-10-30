namespace ByteShelf.Server.Storage.Models
{
    /// <summary>What counts against a shelf's quota.</summary>
    public class ShelfUsage
    {
        /// <summary>The size of every file row on the shelf, the trash included.</summary>
        public required long UsedBytes { get; init; }

        /// <summary>The declared size of every upload in progress.</summary>
        public required long ReservedBytes { get; init; }

        /// <summary>How many live files the shelf holds.</summary>
        public required int FileCount { get; init; }

        public long TotalBytes => UsedBytes + ReservedBytes;
    }
}
