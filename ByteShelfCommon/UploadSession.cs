namespace ByteShelfCommon
{
    /// <summary>A resumable upload in progress.</summary>
    public sealed class UploadSession
    {
        /// <summary>The upload's id.</summary>
        public required Guid Id { get; init; }

        /// <summary>The id of the shelf the file goes to.</summary>
        public required Guid ShelfId { get; init; }

        /// <summary>Where the file will be stored; null for a swap, whose file stays where it is.</summary>
        /// <remarks>
        /// Exactly one of this and <see cref="SwapFileId"/> is set. Two flat fields rather than a typed
        /// target, as the server keeps it: this is the API's JSON, read by clients in any language.
        /// </remarks>
        public string? Path { get; init; }

        /// <summary>The file whose contents a swap replaces in place; null for an upload of a new file.</summary>
        public Guid? SwapFileId { get; init; }

        /// <summary>The MIME type that will be stored.</summary>
        public required string ContentType { get; init; }

        /// <summary>The size declared when the upload started.</summary>
        public required long SizeBytes { get; init; }

        /// <summary>How many bytes the server holds; the offset the next chunk must start at.</summary>
        public required long ReceivedBytes { get; init; }

        /// <summary>Whether a file already at <see cref="Path"/> will be replaced; false for a swap.</summary>
        public required bool Overwrite { get; init; }

        /// <summary>When the upload started.</summary>
        public required DateTimeOffset CreatedAt { get; init; }

        /// <summary>When the upload is removed if no more bytes arrive.</summary>
        public required DateTimeOffset ExpiresAt { get; init; }
    }
}
