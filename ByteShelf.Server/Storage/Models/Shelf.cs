namespace ByteShelf.Server.Storage.Models
{
    /// <summary>A shelf's row: one storage place with its own directories and quota.</summary>
    public class Shelf
    {
        public required Guid Id { get; init; }
        public required string Name { get; init; }

        /// <summary>How many bytes the shelf may hold; null for no limit.</summary>
        public long? QuotaBytes { get; init; }

        public required DateTimeOffset CreatedAt { get; init; }
        public required DateTimeOffset UpdatedAt { get; init; }
    }
}
