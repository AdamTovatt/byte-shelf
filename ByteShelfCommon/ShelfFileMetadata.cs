namespace ByteShelfCommon
{
    /// <summary>
    /// Contains metadata information about a stored file.
    /// </summary>
    /// <remarks>
    /// This class holds all the information about a file except for its actual content.
    /// The content is stored separately in chunks and can be retrieved using the file ID.
    /// </remarks>
    public class ShelfFileMetadata
    {
        /// <summary>
        /// Gets or sets the unique identifier for the file.
        /// </summary>
        /// <remarks>
        /// This ID is used to reference the file in all operations.
        /// </remarks>
        public Guid Id { get; set; }

        /// <summary>
        /// Gets or sets the original filename of the file.
        /// </summary>
        /// <remarks>
        /// This is the filename as it was when the file was originally uploaded.
        /// </remarks>
        public string OriginalFilename { get; set; }

        /// <summary>
        /// Gets or sets the MIME type of the file content.
        /// </summary>
        /// <remarks>
        /// This indicates the type of content stored in the file (e.g., "text/plain", "image/jpeg").
        /// </remarks>
        public string ContentType { get; set; }

        /// <summary>
        /// Gets or sets the total size of the file in bytes.
        /// </summary>
        /// <remarks>
        /// This represents the complete size of the original file, regardless of how it's chunked in storage.
        /// </remarks>
        public long FileSize { get; set; }

        /// <summary>
        /// Gets or sets the UTC timestamp when the file was created.
        /// </summary>
        /// <remarks>
        /// This timestamp is automatically set when the file is first stored.
        /// </remarks>
        public DateTimeOffset CreatedAt { get; set; }

        /// <summary>
        /// Gets or sets the list of chunk IDs that make up this file.
        /// </summary>
        /// <remarks>
        /// Each chunk ID references a separate piece of the file's content.
        /// The chunks should be read in order to reconstruct the complete file.
        /// </remarks>
        public List<Guid> ChunkIds { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ShelfFileMetadata"/> class.
        /// </summary>
        /// <param name="id">The unique identifier for the file.</param>
        /// <param name="originalFilename">The original filename of the file.</param>
        /// <param name="contentType">The MIME type of the file content.</param>
        /// <param name="fileSize">The total size of the file in bytes.</param>
        /// <param name="chunkIds">The list of chunk IDs that make up this file.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="originalFilename"/>, <paramref name="contentType"/>, or <paramref name="chunkIds"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="originalFilename"/> or <paramref name="contentType"/> is empty.</exception>
        /// <remarks>
        /// The <see cref="CreatedAt"/> property is automatically set to the current UTC time.
        /// </remarks>
        public ShelfFileMetadata(
            Guid id,
            string originalFilename,
            string contentType,
            long fileSize,
            List<Guid> chunkIds)
        {
            Id = id;
            OriginalFilename = originalFilename ?? throw new ArgumentNullException(nameof(originalFilename));
            ContentType = contentType ?? throw new ArgumentNullException(nameof(contentType));
            FileSize = fileSize;
            ChunkIds = chunkIds ?? throw new ArgumentNullException(nameof(chunkIds));
            CreatedAt = DateTimeOffset.UtcNow;
        }
    }
}