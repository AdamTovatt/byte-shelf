namespace ByteShelfCommon
{
    /// <summary>One shelf a key works on, and what it may do there.</summary>
    public sealed class ShelfKeyGrantInfo
    {
        /// <summary>The key's id.</summary>
        public required Guid KeyId { get; init; }

        /// <summary>The shelf's id.</summary>
        public required Guid ShelfId { get; init; }

        /// <summary>The shelf's name.</summary>
        public required string ShelfName { get; init; }

        /// <summary>What the key may do on the shelf.</summary>
        public required ShelfPermission Permission { get; init; }

        /// <summary>When the key was given the shelf.</summary>
        public required DateTimeOffset CreatedAt { get; init; }

        /// <summary>When the permission last changed.</summary>
        public required DateTimeOffset UpdatedAt { get; init; }
    }
}
