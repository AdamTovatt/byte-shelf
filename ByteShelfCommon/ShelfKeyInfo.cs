namespace ByteShelfCommon
{
    /// <summary>A shelf key as it is listed: never its secret, which is shown once when it is created.</summary>
    public sealed class ShelfKeyInfo
    {
        /// <summary>The key's id.</summary>
        public required Guid Id { get; init; }

        /// <summary>The id of the one shelf the key works on.</summary>
        public required Guid ShelfId { get; init; }

        /// <summary>A name saying which program holds it.</summary>
        public required string Name { get; init; }

        /// <summary>What the key may do on its shelf.</summary>
        public required ShelfKeyPermission Permission { get; init; }

        /// <summary>When the key was created.</summary>
        public required DateTimeOffset CreatedAt { get; init; }
    }
}
