namespace ByteShelfCommon
{
    /// <summary>One move in a <see cref="MoveEntriesRequest"/>: the file or directory at <see cref="Path"/> to <see cref="ToPath"/>.</summary>
    public sealed class EntryMove
    {
        /// <summary>Where the file or directory is.</summary>
        public required string Path { get; init; }

        /// <summary>Its new path, as <see cref="MoveRequest.ToPath"/>.</summary>
        public required string ToPath { get; init; }
    }
}
