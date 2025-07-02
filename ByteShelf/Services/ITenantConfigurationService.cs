using ByteShelf.Configuration;
using ByteShelfCommon;

namespace ByteShelf.Services
{
    /// <summary>
    /// Service for managing tenant configuration from an external file.
    /// </summary>
    /// <remarks>
    /// This service provides thread-safe access to tenant configuration loaded from an external JSON file.
    /// It supports hot-reloading when the configuration file changes and can create the file if it doesn't exist.
    /// The configuration file path is determined by the BYTESHELF_TENANT_CONFIG_PATH environment variable,
    /// or defaults to "./tenant-config.json" if not set.
    /// </remarks>
    public interface ITenantConfigurationService
    {
        /// <summary>
        /// Gets the current tenant configuration.
        /// </summary>
        /// <returns>The current tenant configuration.</returns>
        /// <remarks>
        /// This method provides thread-safe access to the current configuration.
        /// The configuration is automatically reloaded when the underlying file changes.
        /// </remarks>
        TenantConfiguration GetConfiguration();

        /// <summary>
        /// Adds a new tenant to the configuration.
        /// </summary>
        /// <param name="tenantId">The tenant ID.</param>
        /// <param name="tenantInfo">The tenant information.</param>
        /// <returns><c>true</c> if the tenant was added successfully; otherwise, <c>false</c>.</returns>
        /// <remarks>
        /// This method adds a new tenant to the configuration and persists the changes to the file.
        /// Returns <c>false</c> if a tenant with the same ID already exists.
        /// </remarks>
        Task<bool> AddTenantAsync(string tenantId, TenantInfo tenantInfo);

        /// <summary>
        /// Updates an existing tenant in the configuration.
        /// </summary>
        /// <param name="tenantId">The tenant ID.</param>
        /// <param name="tenantInfo">The updated tenant information.</param>
        /// <returns><c>true</c> if the tenant was updated successfully; otherwise, <c>false</c>.</returns>
        /// <remarks>
        /// This method updates an existing tenant in the configuration and persists the changes to the file.
        /// Returns <c>false</c> if the tenant does not exist.
        /// </remarks>
        Task<bool> UpdateTenantAsync(string tenantId, TenantInfo tenantInfo);

        /// <summary>
        /// Removes a tenant from the configuration.
        /// </summary>
        /// <param name="tenantId">The tenant ID to remove.</param>
        /// <returns><c>true</c> if the tenant was removed successfully; otherwise, <c>false</c>.</returns>
        /// <remarks>
        /// This method removes a tenant from the configuration and persists the changes to the file.
        /// Returns <c>false</c> if the tenant does not exist.
        /// </remarks>
        Task<bool> RemoveTenantAsync(string tenantId);

        /// <summary>
        /// Gets the path to the configuration file.
        /// </summary>
        /// <returns>The path to the configuration file.</returns>
        string GetConfigurationFilePath();

        /// <summary>
        /// Reloads the configuration from the file.
        /// </summary>
        /// <returns><c>true</c> if the configuration was reloaded successfully; otherwise, <c>false</c>.</returns>
        /// <remarks>
        /// This method forces a reload of the configuration from the file.
        /// Returns <c>false</c> if the file cannot be read or parsed.
        /// </remarks>
        Task<bool> ReloadConfigurationAsync();
    }
}