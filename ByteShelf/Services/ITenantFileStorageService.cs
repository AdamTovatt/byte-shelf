using ByteShelfCommon;

namespace ByteShelf.Services
{
    /// <summary>
    /// Service for storing and retrieving files with tenant isolation and quota management.
    /// </summary>
    /// <remarks>
    /// This service extends the basic file storage functionality with tenant isolation,
    /// ensuring that each tenant's files are stored in separate directories and that
    /// storage quotas are enforced. All operations are scoped to a specific tenant.
    /// </remarks>
    public interface ITenantFileStorageService
    {
        /// <summary>
        /// Retrieves metadata for all files belonging to a specific tenant.
        /// </summary>
        /// <param name="tenantId">The tenant ID.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A collection of file metadata for all files belonging to the tenant.</returns>
        Task<IEnumerable<ShelfFileMetadata>> GetFilesAsync(string tenantId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves metadata for a specific file by its ID, scoped to a tenant.
        /// </summary>
        /// <param name="tenantId">The tenant ID.</param>
        /// <param name="fileId">The unique identifier of the file.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>The file metadata, or <c>null</c> if the file does not exist or does not belong to the tenant.</returns>
        Task<ShelfFileMetadata?> GetFileMetadataAsync(string tenantId, Guid fileId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves a chunk by its ID, scoped to a tenant.
        /// </summary>
        /// <param name="tenantId">The tenant ID.</param>
        /// <param name="chunkId">The unique identifier of the chunk.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A stream containing the chunk data.</returns>
        /// <exception cref="FileNotFoundException">Thrown when the specified chunk does not exist or does not belong to the tenant.</exception>
        Task<Stream> GetChunkAsync(string tenantId, Guid chunkId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Saves a chunk with the specified ID, scoped to a tenant.
        /// </summary>
        /// <param name="tenantId">The tenant ID.</param>
        /// <param name="chunkId">The unique identifier for the chunk.</param>
        /// <param name="chunkData">A stream containing the chunk data to be stored.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>The chunk ID that was saved.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="chunkData"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the tenant would exceed their storage quota.</exception>
        Task<Guid> SaveChunkAsync(string tenantId, Guid chunkId, Stream chunkData, CancellationToken cancellationToken = default);

        /// <summary>
        /// Saves file metadata, scoped to a tenant.
        /// </summary>
        /// <param name="tenantId">The tenant ID.</param>
        /// <param name="metadata">The file metadata to be stored.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous save operation.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="metadata"/> is null.</exception>
        Task SaveFileMetadataAsync(string tenantId, ShelfFileMetadata metadata, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes a file and all its associated chunks, scoped to a tenant.
        /// </summary>
        /// <param name="tenantId">The tenant ID.</param>
        /// <param name="fileId">The unique identifier of the file to delete.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous delete operation.</returns>
        Task DeleteFileAsync(string tenantId, Guid fileId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks if a tenant can store a file of the specified size.
        /// </summary>
        /// <param name="tenantId">The tenant ID.</param>
        /// <param name="fileSizeBytes">The size of the file in bytes.</param>
        /// <returns><c>true</c> if the tenant can store the file; otherwise, <c>false</c>.</returns>
        bool CanStoreFile(string tenantId, long fileSizeBytes);
    }
}