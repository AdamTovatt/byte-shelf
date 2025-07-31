namespace ByteShelf.Services
{
    /// <summary>
    /// Service for managing user storage quotas and usage tracking in a filesystem-based architecture.
    /// </summary>
    /// <remarks>
    /// This service provides thread-safe operations for checking storage quotas
    /// and tracking usage per user within tenants. It maintains usage data in memory for
    /// performance and persists it to disk periodically.
    /// </remarks>
    public interface IFileSystemStorageService
    {
        /// <summary>
        /// Checks if a user can store the specified amount of data.
        /// </summary>
        /// <param name="tenantId">The tenant ID.</param>
        /// <param name="userId">The user ID to check.</param>
        /// <param name="sizeBytes">The size of data to be stored in bytes.</param>
        /// <returns><c>true</c> if the user can store the data; otherwise, <c>false</c>.</returns>
        /// <remarks>
        /// This method is thread-safe and will check the current usage against
        /// both the user's individual storage limit and the tenant's overall limit.
        /// If either limit would be exceeded, this method returns <c>false</c>.
        /// </remarks>
        Task<bool> CanStoreDataAsync(string tenantId, string userId, long sizeBytes);

        /// <summary>
        /// Records that a user has stored the specified amount of data.
        /// </summary>
        /// <param name="tenantId">The tenant ID.</param>
        /// <param name="userId">The user ID.</param>
        /// <param name="sizeBytes">The size of data stored in bytes.</param>
        /// <remarks>
        /// This method is thread-safe and atomically updates the user's usage.
        /// The usage is persisted to disk periodically.
        /// </remarks>
        Task RecordStorageUsedAsync(string tenantId, string userId, long sizeBytes);

        /// <summary>
        /// Records that a user has freed the specified amount of data.
        /// </summary>
        /// <param name="tenantId">The tenant ID.</param>
        /// <param name="userId">The user ID.</param>
        /// <param name="sizeBytes">The size of data freed in bytes.</param>
        /// <remarks>
        /// This method is thread-safe and atomically updates the user's usage.
        /// The usage is persisted to disk periodically.
        /// </remarks>
        Task RecordStorageFreedAsync(string tenantId, string userId, long sizeBytes);

        /// <summary>
        /// Gets the current storage usage for a user.
        /// </summary>
        /// <param name="tenantId">The tenant ID.</param>
        /// <param name="userId">The user ID.</param>
        /// <returns>The current storage usage in bytes.</returns>
        Task<long> GetUserUsageAsync(string tenantId, string userId);

        /// <summary>
        /// Gets the current storage usage for all users in a tenant.
        /// </summary>
        /// <param name="tenantId">The tenant ID.</param>
        /// <returns>The total storage usage for all users in the tenant in bytes.</returns>
        Task<long> GetTenantUsageAsync(string tenantId);

        /// <summary>
        /// Gets the storage limit for a user.
        /// </summary>
        /// <param name="tenantId">The tenant ID.</param>
        /// <param name="userId">The user ID.</param>
        /// <returns>The user's storage limit in bytes, or 0 if unlimited.</returns>
        Task<long> GetUserStorageLimitAsync(string tenantId, string userId);

        /// <summary>
        /// Gets the storage limit for a tenant.
        /// </summary>
        /// <param name="tenantId">The tenant ID.</param>
        /// <returns>The tenant's storage limit in bytes, or 0 if unlimited.</returns>
        Task<long> GetTenantStorageLimitAsync(string tenantId);

        /// <summary>
        /// Gets storage usage information for all users in a tenant.
        /// </summary>
        /// <param name="tenantId">The tenant ID.</param>
        /// <returns>A dictionary with user IDs as keys and usage in bytes as values.</returns>
        Task<Dictionary<string, long>> GetTenantUserUsagesAsync(string tenantId);

        /// <summary>
        /// Recalculates storage usage by scanning the filesystem.
        /// This is useful for maintenance and ensuring data consistency.
        /// </summary>
        /// <param name="tenantId">The tenant ID to recalculate, or null for all tenants.</param>
        /// <param name="userId">The user ID to recalculate, or null for all users in the tenant.</param>
        /// <returns>A task representing the recalculation operation.</returns>
        Task RecalculateUsageAsync(string? tenantId = null, string? userId = null);

        /// <summary>
        /// Forces persistence of usage data to disk.
        /// </summary>
        /// <returns>A task representing the persistence operation.</returns>
        Task PersistUsageDataAsync();
    }
}