using ByteShelfCommon;

namespace ByteShelf.Server.Access.Models
{
    /// <summary>A <c>shelf_key</c> row: a program's key to one shelf, stored as the SHA-256 of its secret.</summary>
    public class ShelfKey
    {
        public required Guid Id { get; init; }
        public required Guid ShelfId { get; init; }
        public required string Name { get; init; }
        public required ShelfKeyPermission Permission { get; init; }
        public required string SecretSha256 { get; init; }
        public required DateTimeOffset CreatedAt { get; init; }
    }
}
