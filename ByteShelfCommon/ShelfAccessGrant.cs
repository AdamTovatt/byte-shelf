namespace ByteShelfCommon
{
    /// <summary>One user's access to a shelf.</summary>
    public sealed class ShelfAccessGrant
    {
        /// <summary>The shelf's id.</summary>
        public required Guid ShelfId { get; init; }

        /// <summary>The user's id.</summary>
        public required Guid UserId { get; init; }

        /// <summary>The user's email.</summary>
        public required string Email { get; init; }

        /// <summary>The user's display name.</summary>
        public required string DisplayName { get; init; }

        /// <summary>What the user may do on the shelf.</summary>
        public required ShelfAccessPermission Permission { get; init; }

        /// <summary>When the access was first given.</summary>
        public required DateTimeOffset CreatedAt { get; init; }

        /// <summary>When the permission last changed.</summary>
        public required DateTimeOffset UpdatedAt { get; init; }
    }
}
