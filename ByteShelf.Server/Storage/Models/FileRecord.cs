namespace ByteShelf.Server.Storage.Models
{
    /// <summary>A file's row. Its contents are the object named <see cref="Sha256"/>.</summary>
    public class FileRecord
    {
        public required Guid Id { get; init; }
        public required Guid ShelfId { get; init; }
        public required Guid DirectoryId { get; init; }
        public required string Name { get; init; }
        public required string ContentType { get; init; }
        public required long SizeBytes { get; init; }
        public required string Sha256 { get; init; }
        public required DateTimeOffset CreatedAt { get; init; }
        public required DateTimeOffset UpdatedAt { get; init; }

        /// <summary>Free text about the file, as <see cref="ByteShelfCommon.FileDescription.Normalize"/> left it; null for none.</summary>
        public string? Description { get; init; }
        public DateTimeOffset? DeletedAt { get; init; }
        public Guid? DeletionId { get; init; }
    }
}
