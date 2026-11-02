namespace ByteShelfCommon
{
    /// <summary>Starts a resumable upload. The whole size is reserved against the shelf's quota at once.</summary>
    public sealed class StartUploadRequest
    {
        /// <summary>Where the file will be stored. Missing parent directories are created when it finishes.</summary>
        public required string Path { get; init; }

        /// <summary>The exact size of the contents in bytes.</summary>
        public required long SizeBytes { get; init; }

        /// <summary>The MIME type to store; when left out, the one the path's extension names, else <c>application/octet-stream</c> (<see cref="ContentTypes.ForUpload"/>).</summary>
        public string? ContentType { get; init; }

        /// <summary>Whether a file already at <see cref="Path"/> is replaced (it goes to the trash). Refused otherwise.</summary>
        public bool Overwrite { get; init; }

        /// <summary>
        /// The file's description, at most <see cref="FileDescription.MaxLength"/> characters. Without
        /// one, a file that replaces another keeps the other's. A single upload carries none, since it
        /// has no body but the contents: describe the file afterwards.
        /// </summary>
        public string? Description { get; init; }
    }
}
