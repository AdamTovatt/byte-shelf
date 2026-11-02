namespace ByteShelfClient
{
    /// <summary>How <see cref="IShelfClient.UploadAsync"/> stores a file.</summary>
    public sealed record UploadOptions
    {
        /// <summary>Replace a file already at the path, moving it to the trash; needs write access.</summary>
        public bool Overwrite { get; init; }

        /// <summary>
        /// The media type stored with the file; when left out, the one the path's extension names, else
        /// <c>application/octet-stream</c> (<see cref="ByteShelfCommon.ContentTypes.ForUpload"/>).
        /// </summary>
        public string? ContentType { get; init; }

        /// <summary>
        /// The file's description, at most <see cref="ByteShelfCommon.FileDescription.MaxLength"/>
        /// characters. A file replaced without one keeps the replaced file's. Over HTTP a file with one
        /// is always sent as a resumable upload, the kind that carries it.
        /// </summary>
        public string? Description { get; init; }
    }
}
