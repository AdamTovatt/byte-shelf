namespace ByteShelfCommon
{
    /// <summary>One deletion that can still be restored: a file, or a directory with what was in it.</summary>
    public sealed class TrashEntry
    {
        /// <summary>Identifies the deletion; restore by this id.</summary>
        public required Guid DeletionId { get; init; }

        /// <summary>Whether a file or a directory was deleted.</summary>
        public required TrashEntryKind Kind { get; init; }

        /// <summary>The id of the deleted file or directory.</summary>
        public required Guid EntryId { get; init; }

        /// <summary>Where it was when it was deleted.</summary>
        public required string Path { get; init; }

        /// <summary>When it was deleted.</summary>
        public required DateTimeOffset DeletedAt { get; init; }

        /// <summary>When it is purged for good, unless restored first.</summary>
        public required DateTimeOffset PurgesAt { get; init; }

        /// <summary>How many files the deletion removed.</summary>
        public required int FileCount { get; init; }

        /// <summary>The total size of those files in bytes.</summary>
        public required long SizeBytes { get; init; }
    }
}
