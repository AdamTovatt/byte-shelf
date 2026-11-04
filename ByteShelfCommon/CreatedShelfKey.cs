namespace ByteShelfCommon
{
    /// <summary>A key just created, with its secret. The server keeps only a hash, so this is the one time it is shown.</summary>
    public sealed class CreatedShelfKey
    {
        /// <summary>The key as it will be listed.</summary>
        public required ShelfKeyInfo Key { get; init; }

        /// <summary>What the program sends in the <see cref="ShelfKeys.HeaderName"/> header.</summary>
        public required string Secret { get; init; }
    }
}
