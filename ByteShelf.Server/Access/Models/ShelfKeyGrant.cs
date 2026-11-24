using ByteShelfCommon;

namespace ByteShelf.Server.Access.Models
{
    /// <summary>A <c>shelf_key_grant</c> row: one key's access to one shelf.</summary>
    public class ShelfKeyGrant
    {
        public required Guid Id { get; init; }
        public required Guid KeyId { get; init; }
        public required Guid ShelfId { get; init; }
        public required ShelfPermission Permission { get; init; }
        public required DateTimeOffset CreatedAt { get; init; }
        public required DateTimeOffset UpdatedAt { get; init; }
    }
}
