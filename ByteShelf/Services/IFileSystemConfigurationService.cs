using ByteShelfCommon.FileSystem;

namespace ByteShelf.Services
{
    /// <summary>
    /// Interface for filesystem-based configuration service that manages tenants and users
    /// through the filesystem structure instead of external configuration files.
    /// </summary>
    public interface IFileSystemConfigurationService
    {
        // Tenant operations
        Task<TenantConfig?> GetTenantAsync(string tenantId);
        Task<IEnumerable<TenantConfig>> GetAllTenantsAsync();
        Task<bool> CreateTenantAsync(string tenantId, string displayName, long storageLimitBytes);
        Task<bool> UpdateTenantAsync(string tenantId, UpdateTenantRequest request);
        Task<bool> DeleteTenantAsync(string tenantId);
        Task<bool> TenantExistsAsync(string tenantId);

        // User operations
        Task<UserConfig?> GetUserAsync(string tenantId, string userId);
        Task<UserConfig?> GetUserByApiKeyAsync(string apiKey);
        Task<IEnumerable<UserConfig>> GetUsersForTenantAsync(string tenantId);
        Task<bool> CreateUserAsync(string tenantId, CreateUserRequest request);
        Task<bool> UpdateUserAsync(string tenantId, string userId, UpdateUserRequest request);
        Task<bool> DeleteUserAsync(string tenantId, string userId);
        Task<bool> UserExistsAsync(string tenantId, string userId);

        // Authentication
        Task<(UserConfig? user, TenantConfig? tenant)> AuthenticateAsync(string apiKey);

        // Utility methods
        Task<string> GenerateApiKeyAsync();
        Task RefreshCacheAsync();
        void StartFileWatching();
        void StopFileWatching();
    }
}