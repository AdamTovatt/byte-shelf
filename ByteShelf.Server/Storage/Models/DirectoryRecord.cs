using System.Text.Json.Serialization;

namespace ByteShelf.Server.Storage.Models
{
    /// <summary>A directory's row. The shelf's root has no parent and an empty name.</summary>
    public class DirectoryRecord
    {
        public required Guid Id { get; init; }
        public required Guid ShelfId { get; init; }
        public Guid? ParentId { get; init; }
        public required string Name { get; init; }
        public required DateTimeOffset CreatedAt { get; init; }
        public required DateTimeOffset UpdatedAt { get; init; }
        public DateTimeOffset? DeletedAt { get; init; }
        public Guid? DeletionId { get; init; }

        [JsonIgnore]
        public bool IsRoot => ParentId == null;
    }
}
