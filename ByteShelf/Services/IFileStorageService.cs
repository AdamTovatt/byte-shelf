using ByteShelfCommon;

namespace ByteShelf.Services
{
    /// <summary>
    /// Interface for file storage operations with tenant isolation.
    /// </summary>
    /// <remarks>
    /// This interface defines the contract for file storage operations that support
    /// tenant isolation. All operations are scoped to specific tenants, ensuring
    /// data separation and quota enforcement.
    /// </remarks>
    public interface IFileStorageService
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
        /// <returns>A task with a nullable bool that represents the asynchronous delete operation. The bool value indicates success state. If the bool is null it means the file was not found.</returns>
        Task<bool?> DeleteFileAsync(string tenantId, Guid fileId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes all files and their associated chunks for a specific tenant.
        /// </summary>
        /// <param name="tenantId">The tenant ID.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>The number of files that were deleted.</returns>
        Task<int> DeleteAllFilesAsync(string tenantId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets a complete file stream by reconstructing it from all its chunks.
        /// </summary>
        /// <param name="tenantId">The tenant ID.</param>
        /// <param name="fileId">The unique identifier of the file.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A stream containing the complete file data.</returns>
        /// <exception cref="FileNotFoundException">Thrown when the specified file does not exist or does not belong to the tenant.</exception>
        Task<Stream> GetFileStreamAsync(string tenantId, Guid fileId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks if a tenant can store a file of the specified size.
        /// </summary>
        /// <param name="tenantId">The tenant ID.</param>
        /// <param name="fileSizeBytes">The size of the file in bytes.</param>
        /// <returns><c>true</c> if the tenant can store the file; otherwise, <c>false</c>.</returns>
        bool CanStoreFile(string tenantId, long fileSizeBytes);

        /// <summary>
        /// Deletes all files and their associated chunks for a tenant and all its descendant tenants.
        /// </summary>
        /// <param name="tenantId">The tenant ID.</param>
        /// <param name="descendantTenantIds">The IDs of all descendant tenants to delete files for.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>The total number of files that were deleted across all tenants.</returns>
        Task<int> DeleteAllFilesRecursivelyAsync(string tenantId, IEnumerable<string> descendantTenantIds, CancellationToken cancellationToken = default);
    }
}