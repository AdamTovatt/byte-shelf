namespace ByteShelfCommon
{
    /// <summary>
    /// Provides a stream for accessing file content.
    /// </summary>
    /// <remarks>
    /// This interface abstracts the source of file content, allowing different implementations
    /// to provide content from various sources (e.g., HTTP streams, local files, memory).
    /// The returned stream should be disposed by the caller when no longer needed.
    /// </remarks>
    public interface IContentProvider
    {
        /// <summary>
        /// Gets a stream for reading the file content.
        /// </summary>
        /// <returns>A stream that can be used to read the file content.</returns>
        /// <remarks>
        /// Each call to this method should return a new stream instance.
        /// The caller is responsible for disposing the returned stream.
        /// </remarks>
        Stream GetStream();
    }
}