namespace ByteShelfCommon
{
    /// <summary>
    /// Defines operations for storing and retrieving files with automatic chunking support.
    /// </summary>
    /// <remarks>
    /// This interface provides a high-level abstraction for file storage operations,
    /// handling the complexity of chunking large files automatically. Implementations
    /// may use different storage backends (e.g., HTTP API, local filesystem, cloud storage).
    /// </remarks>
    public interface IShelfFileProvider
    {
        /// <summary>
        /// Retrieves metadata for all stored files.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A collection of file metadata for all stored files.</returns>
        /// <remarks>
        /// This method returns metadata only, not the actual file content.
        /// Use <see cref="ReadFileAsync(Guid, CancellationToken)"/> to retrieve the full file with content.
        /// </remarks>
        Task<IEnumerable<ShelfFileMetadata>> GetFilesAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads a file by its ID, returning both metadata and content.
        /// </summary>
        /// <param name="fileId">The unique identifier of the file to read.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A <see cref="ShelfFile"/> containing the file metadata and content stream.</returns>
        /// <exception cref="FileNotFoundException">Thrown when the specified file ID does not exist.</exception>
        /// <remarks>
        /// The returned <see cref="ShelfFile"/> should be disposed when no longer needed
        /// to properly clean up the content stream.
        /// </remarks>
        Task<ShelfFile> ReadFileAsync(Guid fileId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Writes a file to storage, automatically chunking it if necessary.
        /// </summary>
        /// <param name="originalFilename">The original filename of the file being stored.</param>
        /// <param name="contentType">The MIME type of the file content.</param>
        /// <param name="content">A stream containing the file content to be stored.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>The unique identifier assigned to the stored file.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="originalFilename"/>, <paramref name="contentType"/>, or <paramref name="content"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="originalFilename"/> or <paramref name="contentType"/> is empty.</exception>
        /// <remarks>
        /// The file will be automatically split into chunks if it exceeds the configured chunk size.
        /// The content stream will be read from its current position to the end.
        /// </remarks>
        Task<Guid> WriteFileAsync(
            string originalFilename,
            string contentType,
            Stream content,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes a file and all its associated chunks from storage.
        /// </summary>
        /// <param name="fileId">The unique identifier of the file to delete.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous delete operation.</returns>
        /// <remarks>
        /// This operation is idempotent - deleting a non-existent file will not throw an exception.
        /// </remarks>
        Task DeleteFileAsync(Guid fileId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves metadata for all stored files from a specific tenant.
        /// </summary>
        /// <param name="targetTenantId">The ID of the tenant whose files to retrieve.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A collection of file metadata for all stored files in the specified tenant.</returns>
        /// <remarks>
        /// This method returns metadata only, not the actual file content.
        /// Use <see cref="ReadFileForTenantAsync(string, Guid, CancellationToken)"/> to retrieve the full file with content.
        /// The authenticated tenant must have access to the specified tenant (either be the same tenant or a parent).
        /// </remarks>
        Task<IEnumerable<ShelfFileMetadata>> GetFilesForTenantAsync(string targetTenantId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads a file by its ID from a specific tenant, returning both metadata and content.
        /// </summary>
        /// <param name="targetTenantId">The ID of the tenant whose file to read.</param>
        /// <param name="fileId">The unique identifier of the file to read.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A <see cref="ShelfFile"/> containing the file metadata and content stream.</returns>
        /// <exception cref="FileNotFoundException">Thrown when the specified file ID does not exist.</exception>
        /// <remarks>
        /// The authenticated tenant must have access to the specified tenant (either be the same tenant or a parent).
        /// The returned <see cref="ShelfFile"/> should be disposed when no longer needed
        /// to properly clean up the content stream.
        /// </remarks>
        Task<ShelfFile> ReadFileForTenantAsync(string targetTenantId, Guid fileId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Writes a file to a specific tenant in storage, automatically chunking it if necessary.
        /// </summary>
        /// <param name="targetTenantId">The ID of the tenant for which to write the file.</param>
        /// <param name="originalFilename">The original filename of the file being stored.</param>
        /// <param name="contentType">The MIME type of the file content.</param>
        /// <param name="content">A stream containing the file content to be stored.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>The unique identifier assigned to the stored file.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="targetTenantId"/>, <paramref name="originalFilename"/>, <paramref name="contentType"/>, or <paramref name="content"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="targetTenantId"/>, <paramref name="originalFilename"/>, or <paramref name="contentType"/> is empty.</exception>
        /// <remarks>
        /// The authenticated tenant must have access to the specified tenant (either be the same tenant or a parent).
        /// The file will be automatically split into chunks if it exceeds the configured chunk size.
        /// The content stream will be read from its current position to the end.
        /// </remarks>
        Task<Guid> WriteFileForTenantAsync(
            string targetTenantId,
            string originalFilename,
            string contentType,
            Stream content,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes a file and all its associated chunks from a specific tenant in storage.
        /// </summary>
        /// <param name="targetTenantId">The ID of the tenant whose file to delete.</param>
        /// <param name="fileId">The unique identifier of the file to delete.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous delete operation.</returns>
        /// <remarks>
        /// The authenticated tenant must have access to the specified tenant (either be the same tenant or a parent).
        /// This operation is idempotent - deleting a non-existent file will not throw an exception.
        /// </remarks>
        Task DeleteFileForTenantAsync(string targetTenantId, Guid fileId, CancellationToken cancellationToken = default);
    }
}