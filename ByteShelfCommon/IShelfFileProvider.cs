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
        /// Reads a file by its ID, returning both metadata and content with configurable retrieval method.
        /// </summary>
        /// <param name="fileId">The unique identifier of the file to read.</param>
        /// <param name="useChunked">Whether to use the chunked endpoint approach. Defaults to false for better performance.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A <see cref="ShelfFile"/> containing the file metadata and content stream.</returns>
        /// <exception cref="FileNotFoundException">Thrown when the specified file ID does not exist.</exception>
        /// <remarks>
        /// When useChunked is false (default), this method uses the efficient single endpoint approach.
        /// When useChunked is true, it uses the chunked approach with separate metadata and chunk endpoints.
        /// The returned <see cref="ShelfFile"/> should be disposed when no longer needed.
        /// </remarks>
        Task<ShelfFile> ReadFileAsync(Guid fileId, bool useChunked, CancellationToken cancellationToken = default);

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
        /// Writes a file to storage with quota checking, automatically chunking it if necessary.
        /// </summary>
        /// <param name="originalFilename">The original filename of the file being stored.</param>
        /// <param name="contentType">The MIME type of the file content.</param>
        /// <param name="content">A stream containing the file content to be stored.</param>
        /// <param name="checkQuotaFirst">Whether to check quota before uploading. Defaults to true.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>The unique identifier assigned to the stored file.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="originalFilename"/>, <paramref name="contentType"/>, or <paramref name="content"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="originalFilename"/> or <paramref name="contentType"/> is empty.</exception>
        /// <exception cref="InvalidOperationException">Thrown when quota is exceeded.</exception>
        /// <remarks>
        /// This method extends the base implementation with optional quota checking.
        /// If <paramref name="checkQuotaFirst"/> is true, it will check if the file can be stored
        /// before attempting the upload, helping to avoid failed uploads due to quota limits.
        /// </remarks>
        Task<Guid> WriteFileWithQuotaCheckAsync(
            string originalFilename,
            string contentType,
            Stream content,
            bool checkQuotaFirst = true,
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

        /// <summary>
        /// Gets storage usage information for the authenticated tenant.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>Storage usage information including current usage and limits.</returns>
        /// <remarks>
        /// This method provides information about the tenant's storage usage including current usage and limits.
        /// </remarks>
        Task<TenantStorageInfo> GetStorageInfoAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks if the current tenant can store a file of the specified size.
        /// </summary>
        /// <param name="fileSizeBytes">The size of the file to check in bytes.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>Information about whether the file can be stored.</returns>
        /// <remarks>
        /// This method allows clients to check if they can store a file of a specific size before attempting to upload it.
        /// </remarks>
        Task<QuotaCheckResult> CanStoreFileAsync(long fileSizeBytes, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets information about the current tenant including admin status and display name.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>Tenant information including admin status, display name, and storage details.</returns>
        /// <remarks>
        /// This method provides information about the tenant including their admin status, which is useful
        /// for frontend applications to determine what UI controls to show.
        /// </remarks>
        Task<TenantInfoResponse> GetTenantInfoAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets all subtenants of the current tenant.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A dictionary of subtenants keyed by tenant ID.</returns>
        /// <remarks>
        /// Returns an empty dictionary if the current tenant has no subtenants.
        /// </remarks>
        Task<Dictionary<string, TenantInfoResponse>> GetSubTenantsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets information about a specific subtenant.
        /// </summary>
        /// <param name="subTenantId">The subtenant ID.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>The subtenant information.</returns>
        /// <exception cref="FileNotFoundException">Thrown when the specified subtenant does not exist.</exception>
        Task<TenantInfoResponse> GetSubTenantAsync(string subTenantId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets all subtenants under a specific subtenant.
        /// </summary>
        /// <param name="parentSubtenantId">The parent subtenant ID.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A dictionary of subtenant information.</returns>
        /// <exception cref="ArgumentException">Thrown when parent subtenant ID is null or empty.</exception>
        /// <exception cref="FileNotFoundException">Thrown when the parent subtenant does not exist.</exception>
        /// <remarks>
        /// The authenticated tenant must have access to the parent subtenant.
        /// Returns an empty dictionary if the parent subtenant has no subtenants.
        /// </remarks>
        Task<Dictionary<string, TenantInfoResponse>> GetSubTenantsUnderSubTenantAsync(string parentSubtenantId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates a new subtenant under the current tenant.
        /// </summary>
        /// <param name="displayName">The display name for the subtenant.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>The ID of the created subtenant.</returns>
        /// <exception cref="ArgumentException">Thrown when display name is null or empty.</exception>
        /// <exception cref="InvalidOperationException">Thrown when maximum depth is reached.</exception>
        /// <remarks>
        /// The subtenant will have a unique ID and API key generated automatically.
        /// </remarks>
        Task<string> CreateSubTenantAsync(string displayName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates a new subtenant under a specific subtenant.
        /// </summary>
        /// <param name="parentSubtenantId">The parent subtenant ID.</param>
        /// <param name="displayName">The display name for the subtenant.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>The ID of the created subtenant.</returns>
        /// <exception cref="ArgumentException">Thrown when parent subtenant ID or display name is null or empty.</exception>
        /// <exception cref="InvalidOperationException">Thrown when maximum depth is reached.</exception>
        /// <exception cref="FileNotFoundException">Thrown when the parent subtenant does not exist.</exception>
        /// <remarks>
        /// The subtenant will have a unique ID and API key generated automatically.
        /// The authenticated tenant must have access to the parent subtenant.
        /// </remarks>
        Task<string> CreateSubTenantUnderSubTenantAsync(string parentSubtenantId, string displayName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Updates the storage limit of a subtenant.
        /// </summary>
        /// <param name="subTenantId">The subtenant ID.</param>
        /// <param name="storageLimitBytes">The new storage limit in bytes.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <exception cref="ArgumentException">Thrown when subtenant ID is null or empty, or storage limit is negative.</exception>
        /// <exception cref="FileNotFoundException">Thrown when the specified subtenant does not exist.</exception>
        /// <remarks>
        /// The new limit cannot exceed the parent tenant's limit.
        /// </remarks>
        Task UpdateSubTenantStorageLimitAsync(string subTenantId, long storageLimitBytes, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes a subtenant.
        /// </summary>
        /// <param name="subTenantId">The subtenant ID.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <exception cref="ArgumentException">Thrown when subtenant ID is null or empty.</exception>
        /// <exception cref="FileNotFoundException">Thrown when the specified subtenant does not exist.</exception>
        /// <remarks>
        /// This operation cannot be undone.
        /// </remarks>
        Task DeleteSubTenantAsync(string subTenantId, CancellationToken cancellationToken = default);
    }
}