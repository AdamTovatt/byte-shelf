using ByteShelfCommon;

namespace ByteShelf.Server.Access.Models
{
    /// <summary>A <c>shelf_grant</c> row: one user's access to one shelf.</summary>
    public class ShelfGrant
    {
        public required Guid Id { get; init; }
        public required Guid ShelfId { get; init; }
        public required Guid UserId { get; init; }
        public required ShelfAccessPermission Permission { get; init; }
        public required DateTimeOffset CreatedAt { get; init; }
        public required DateTimeOffset UpdatedAt { get; init; }
    }
}
