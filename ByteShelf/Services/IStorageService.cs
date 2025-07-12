namespace ByteShelf.Services
{
    /// <summary>
    /// Service for managing tenant storage quotas and usage tracking.
    /// </summary>
    /// <remarks>
    /// This service provides thread-safe operations for checking storage quotas
    /// and tracking usage per tenant. It maintains usage data in memory for
    /// performance and persists it to disk periodically.
    /// </remarks>
    public interface IStorageService
    {
        /// <summary>
        /// Checks if a tenant can store the specified amount of data.
        /// </summary>
        /// <param name="tenantId">The tenant ID to check.</param>
        /// <param name="sizeBytes">The size of data to be stored in bytes.</param>
        /// <returns><c>true</c> if the tenant can store the data; otherwise, <c>false</c>.</returns>
        /// <remarks>
        /// This method is thread-safe and will check the current usage against
        /// the tenant's storage limit. If the tenant would exceed their limit
        /// by storing the specified amount of data, this method returns <c>false</c>.
        /// </remarks>
        bool CanStoreData(string tenantId, long sizeBytes);

        /// <summary>
        /// Records that a tenant has stored the specified amount of data.
        /// </summary>
        /// <param name="tenantId">The tenant ID.</param>
        /// <param name="sizeBytes">The size of data stored in bytes.</param>
        /// <remarks>
        /// This method is thread-safe and atomically updates the tenant's usage.
        /// The usage is persisted to disk periodically.
        /// </remarks>
        void RecordStorageUsed(string tenantId, long sizeBytes);

        /// <summary>
        /// Records that a tenant has freed the specified amount of data.
        /// </summary>
        /// <param name="tenantId">The tenant ID.</param>
        /// <param name="sizeBytes">The size of data freed in bytes.</param>
        /// <remarks>
        /// This method is thread-safe and atomically updates the tenant's usage.
        /// The usage is persisted to disk periodically.
        /// </remarks>
        void RecordStorageFreed(string tenantId, long sizeBytes);

        /// <summary>
        /// Gets the current storage usage for a tenant.
        /// </summary>
        /// <param name="tenantId">The tenant ID.</param>
        /// <returns>The current storage usage in bytes.</returns>
        long GetCurrentUsage(string tenantId);

        /// <summary>
        /// Gets the storage limit for a tenant.
        /// </summary>
        /// <param name="tenantId">The tenant ID.</param>
        /// <returns>The storage limit in bytes, or 0 if the tenant is not found.</returns>
        long GetStorageLimit(string tenantId);

        /// <summary>
        /// Gets the total storage usage for a tenant including all subtenants recursively.
        /// </summary>
        /// <param name="tenantId">The tenant ID.</param>
        /// <returns>The total storage usage including subtenants in bytes.</returns>
        long GetTotalUsageIncludingSubTenants(string tenantId);
    }
}