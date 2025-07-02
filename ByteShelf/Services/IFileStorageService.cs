using ByteShelfCommon;

namespace ByteShelf.Services
{
    /// <summary>
    /// Defines operations for storing and retrieving files and chunks in the ByteShelf storage system.
    /// </summary>
    /// <remarks>
    /// This interface provides low-level storage operations for the ByteShelf file storage system.
    /// It handles the storage and retrieval of file metadata and individual chunks.
    /// Implementations may use different storage backends (e.g., local filesystem, cloud storage).
    /// </remarks>
    public interface IFileStorageService
    {
        /// <summary>
        /// Retrieves metadata for all stored files.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A collection of file metadata for all stored files.</returns>
        /// <remarks>
        /// This method scans the storage for all metadata files and deserializes them.
        /// Corrupted metadata files are logged and skipped.
        /// </remarks>
        Task<IEnumerable<ShelfFileMetadata>> GetFilesAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves metadata for a specific file by its ID.
        /// </summary>
        /// <param name="fileId">The unique identifier of the file.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>The file metadata, or <c>null</c> if the file does not exist.</returns>
        /// <remarks>
        /// This method looks for a metadata file with the specified ID in the storage.
        /// Returns <c>null</c> if no metadata file is found.
        /// </remarks>
        Task<ShelfFileMetadata?> GetFileMetadataAsync(Guid fileId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves a chunk by its ID.
        /// </summary>
        /// <param name="chunkId">The unique identifier of the chunk.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A stream containing the chunk data.</returns>
        /// <exception cref="FileNotFoundException">Thrown when the specified chunk does not exist.</exception>
        /// <remarks>
        /// The returned stream should be disposed when no longer needed.
        /// </remarks>
        Task<Stream> GetChunkAsync(Guid chunkId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Saves a chunk with the specified ID.
        /// </summary>
        /// <param name="chunkId">The unique identifier for the chunk.</param>
        /// <param name="chunkData">A stream containing the chunk data to be stored.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>The chunk ID that was saved.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="chunkData"/> is null.</exception>
        /// <remarks>
        /// The chunk data stream will be read from its current position to the end.
        /// If a chunk with the same ID already exists, it will be overwritten.
        /// </remarks>
        Task<Guid> SaveChunkAsync(Guid chunkId, Stream chunkData, CancellationToken cancellationToken = default);

        /// <summary>
        /// Saves file metadata.
        /// </summary>
        /// <param name="metadata">The file metadata to be stored.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous save operation.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="metadata"/> is null.</exception>
        /// <remarks>
        /// The metadata is serialized to JSON and stored in a file named with the file ID.
        /// If metadata for the same file ID already exists, it will be overwritten.
        /// </remarks>
        Task SaveFileMetadataAsync(ShelfFileMetadata metadata, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes a file and all its associated chunks.
        /// </summary>
        /// <param name="fileId">The unique identifier of the file to delete.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous delete operation.</returns>
        /// <remarks>
        /// This method deletes both the file metadata and all chunks associated with the file.
        /// If the file does not exist, no exception is thrown (idempotent operation).
        /// </remarks>
        Task DeleteFileAsync(Guid fileId, CancellationToken cancellationToken = default);
    }
}