namespace ByteShelfCommon
{
    /// <summary>
    /// A link on a shelf that has not run out or been revoked, as listed: never its URL, which only its
    /// maker was given.
    /// </summary>
    public sealed class ShelfLink
    {
        /// <summary>The link's id, which revokes it.</summary>
        public required Guid Id { get; init; }

        /// <summary>Whether the link reads a file or stores one.</summary>
        public required ShelfLinkKind Kind { get; init; }

        /// <summary>
        /// A download link's file as it is now, null while the file is in the trash; an upload link's
        /// place to store at.
        /// </summary>
        public string? Path { get; init; }

        /// <summary>The file a download link reads; null for an upload link.</summary>
        public Guid? FileId { get; init; }

        /// <summary>Whether an upload link replaces a file already at <see cref="Path"/>; false for a download link.</summary>
        public required bool Overwrite { get; init; }

        /// <summary>The description an upload link stores its file with; null for none, and for a download link.</summary>
        public string? Description { get; init; }

        /// <summary>Who made the link, whose access it works with.</summary>
        public required LinkMaker CreatedBy { get; init; }

        /// <summary>When the link was made.</summary>
        public required DateTimeOffset CreatedAt { get; init; }

        /// <summary>When the link stops working.</summary>
        public required DateTimeOffset ExpiresAt { get; init; }
    }
}
