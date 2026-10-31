namespace ByteShelfClient
{
    /// <summary>How <see cref="IShelfClient.UploadAsync"/> stores a file.</summary>
    public sealed class UploadOptions
    {
        /// <summary>Replace a file already at the path, moving it to the trash; needs write access.</summary>
        public bool Overwrite { get; init; }

        /// <summary>The media type stored with the file; <c>application/octet-stream</c> when left out.</summary>
        public string? ContentType { get; init; }
    }
}
