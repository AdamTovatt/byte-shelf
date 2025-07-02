namespace ByteShelfCommon
{
    /// <summary>
    /// Represents a file with its metadata and content stream.
    /// </summary>
    /// <remarks>
    /// This class combines file metadata with a content provider that can supply the actual file content.
    /// The content is accessed through a stream, which should be disposed when no longer needed.
    /// This class implements <see cref="IDisposable"/> to ensure proper cleanup of the content provider.
    /// </remarks>
    public class ShelfFile : IDisposable
    {
        /// <summary>
        /// Gets the metadata information for this file.
        /// </summary>
        /// <remarks>
        /// This contains all the file information except the actual content.
        /// </remarks>
        public ShelfFileMetadata Metadata { get; }

        private readonly IContentProvider _contentProvider;

        /// <summary>
        /// Initializes a new instance of the <see cref="ShelfFile"/> class.
        /// </summary>
        /// <param name="metadata">The metadata information for the file.</param>
        /// <param name="contentProvider">The content provider that can supply the file's content stream.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="metadata"/> or <paramref name="contentProvider"/> is null.</exception>
        /// <remarks>
        /// The content provider will be disposed when this <see cref="ShelfFile"/> is disposed.
        /// </remarks>
        public ShelfFile(
            ShelfFileMetadata metadata,
            IContentProvider contentProvider)
        {
            Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            _contentProvider = contentProvider ?? throw new ArgumentNullException(nameof(contentProvider));
        }

        /// <summary>
        /// Gets a stream for reading the file content.
        /// </summary>
        /// <returns>A stream that can be used to read the file content.</returns>
        /// <remarks>
        /// Each call to this method returns a new stream instance.
        /// The returned stream should be disposed when no longer needed.
        /// </remarks>
        public Stream GetContentStream()
        {
            return _contentProvider.GetStream();
        }

        /// <summary>
        /// Releases the resources used by this <see cref="ShelfFile"/>.
        /// </summary>
        /// <remarks>
        /// This method disposes the content provider if it implements <see cref="IDisposable"/>.
        /// </remarks>
        public void Dispose()
        {
            if (_contentProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }
            GC.SuppressFinalize(this);
        }
    }
}