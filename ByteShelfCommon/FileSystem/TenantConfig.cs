using System.Text.Json.Serialization;

namespace ByteShelfCommon.FileSystem
{
    /// <summary>
    /// Represents the configuration for a tenant stored in the filesystem.
    /// This configuration is stored in /storage/tenantId/tenant-config.json
    /// </summary>
    public class TenantConfig
    {
        /// <summary>
        /// Gets or sets the unique identifier for the tenant.
        /// This should match the directory name.
        /// </summary>
        public string TenantId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the human-readable display name for the tenant.
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the maximum storage allowed for this tenant in bytes.
        /// Set to 0 for unlimited storage.
        /// </summary>
        public long StorageLimitBytes { get; set; }

        /// <summary>
        /// Gets or sets whether this tenant is active and can be used.
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Gets or sets when this tenant was created.
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Gets or sets when this tenant configuration was last modified.
        /// </summary>
        public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Gets or sets additional metadata for the tenant.
        /// </summary>
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
    }
}