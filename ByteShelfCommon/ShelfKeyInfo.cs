namespace ByteShelfCommon
{
    /// <summary>A shelf key as it is listed: never its secret, which is shown once when it is created.</summary>
    public sealed class ShelfKeyInfo
    {
        /// <summary>The key's id.</summary>
        public required Guid Id { get; init; }

        /// <summary>A name saying which program holds it.</summary>
        public required string Name { get; init; }

        /// <summary>The shelves the key works on and what it may do on each, by shelf name. A key always has at least one.</summary>
        public required IReadOnlyList<ShelfKeyGrantInfo> Grants { get; init; }

        /// <summary>When the key was created.</summary>
        public required DateTimeOffset CreatedAt { get; init; }
    }
}
