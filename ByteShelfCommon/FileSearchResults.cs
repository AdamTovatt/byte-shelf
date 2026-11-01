namespace ByteShelfCommon
{
    /// <summary>One page of a <see cref="FileSearch"/>'s files, by path.</summary>
    public sealed class FileSearchResults
    {
        /// <summary>The files found, by path.</summary>
        public required IReadOnlyList<ShelfFile> Files { get; init; }

        /// <summary>What to pass as <see cref="FileSearch.After"/> for the next page; null on the last one.</summary>
        public string? NextCursor { get; init; }
    }
}
