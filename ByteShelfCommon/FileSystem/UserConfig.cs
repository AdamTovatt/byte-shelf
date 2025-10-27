using System.Text.Json.Serialization;

namespace ByteShelfCommon.FileSystem
{
    /// <summary>
    /// Represents the configuration for a user stored in the filesystem.
    /// This configuration is stored in /storage/tenantId/users/userId/user-config.json
    /// </summary>
    public class UserConfig
    {
        /// <summary>
        /// Gets or sets the unique identifier for the user within the tenant.
        /// This should match the directory name.
        /// </summary>
        public string UserId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the tenant ID this user belongs to.
        /// </summary>
        public string TenantId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the API key required for authentication.
        /// This key must be provided in the X-API-Key header for all API requests.
        /// </summary>
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the human-readable display name for the user.
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the email address for the user (optional).
        /// </summary>
        public string Email { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the maximum storage allowed for this user in bytes.
        /// Set to 0 for unlimited storage (within tenant limits).
        /// </summary>
        public long StorageLimitBytes { get; set; }

        /// <summary>
        /// Gets or sets whether this user is active and can authenticate.
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Gets or sets whether this user has administrative privileges within the tenant.
        /// Admin users can manage other users within the same tenant.
        /// </summary>
        public bool IsAdmin { get; set; } = false;

        /// <summary>
        /// Gets or sets when this user was created.
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Gets or sets when this user configuration was last modified.
        /// </summary>
        public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Gets or sets when this user last accessed the system.
        /// </summary>
        public DateTime? LastAccessAt { get; set; }

        /// <summary>
        /// Gets or sets additional metadata for the user.
        /// </summary>
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
    }
}