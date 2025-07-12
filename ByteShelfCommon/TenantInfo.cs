using System.Text.Json.Serialization;

namespace ByteShelfCommon
{
    /// <summary>
    /// Represents configuration information for a tenant.
    /// </summary>
    /// <remarks>
    /// This class contains all the configuration settings for a single tenant,
    /// including authentication credentials, storage limits, and administrative privileges.
    /// </remarks>
    public class TenantInfo
    {
        /// <summary>
        /// Gets or sets the API key required for authentication.
        /// </summary>
        /// <remarks>
        /// This key must be provided in the X-API-Key header for all API requests.
        /// The key should be kept secure and not shared between tenants.
        /// </remarks>
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the maximum storage allowed for this tenant in bytes.
        /// </summary>
        /// <remarks>
        /// Set to 0 for unlimited storage (typically used for admin tenants).
        /// For regular tenants, this should be set to a positive value.
        /// </remarks>
        public long StorageLimitBytes { get; set; }

        /// <summary>
        /// Gets or sets the human-readable display name for the tenant.
        /// </summary>
        /// <remarks>
        /// This name is used for display purposes in admin interfaces and logs.
        /// It does not need to be unique across tenants.
        /// </remarks>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets whether this tenant has administrative privileges.
        /// </summary>
        /// <remarks>
        /// Admin tenants have access to administrative endpoints and can manage
        /// other tenants. They typically have unlimited storage when StorageLimitBytes is 0.
        /// </remarks>
        public bool IsAdmin { get; set; } = false;

        /// <summary>
        /// Gets or sets the parent tenant reference.
        /// </summary>
        /// <remarks>
        /// This property is used for runtime navigation and is not serialized to JSON
        /// to avoid circular references. The parent relationship is stored in the child tenant.
        /// </remarks>
        [JsonIgnore]
        public TenantInfo? Parent { get; set; }

        /// <summary>
        /// Gets or sets the dictionary of subtenants, keyed by tenant ID.
        /// </summary>
        /// <remarks>
        /// Each subtenant is a complete TenantInfo object with its own configuration.
        /// Subtenants inherit access from their parent but have their own storage limits.
        /// </remarks>
        public Dictionary<string, TenantInfo> SubTenants { get; set; } = new Dictionary<string, TenantInfo>();
    }

    /// <summary>
    /// Represents the response from the tenant info endpoint.
    /// </summary>
    /// <remarks>
    /// This class contains both static tenant configuration and dynamic information
    /// like current storage usage. It's used by the GET /api/tenant/info endpoint.
    /// </remarks>
    public class TenantInfoResponse
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TenantInfoResponse"/> class.
        /// </summary>
        /// <param name="tenantId">The unique identifier of the tenant.</param>
        /// <param name="displayName">The human-readable display name for the tenant.</param>
        /// <param name="isAdmin">Whether this tenant has administrative privileges.</param>
        /// <param name="storageLimitBytes">The maximum storage allowed for this tenant in bytes.</param>
        /// <param name="currentUsageBytes">The current storage usage in bytes.</param>
        /// <param name="availableSpaceBytes">The available storage space in bytes.</param>
        /// <param name="usagePercentage">The percentage of storage used (0-100).</param>
        public TenantInfoResponse(
            string tenantId,
            string displayName,
            bool isAdmin,
            long storageLimitBytes,
            long currentUsageBytes,
            long availableSpaceBytes,
            double usagePercentage)
        {
            TenantId = tenantId;
            DisplayName = displayName;
            IsAdmin = isAdmin;
            StorageLimitBytes = storageLimitBytes;
            CurrentUsageBytes = currentUsageBytes;
            AvailableSpaceBytes = availableSpaceBytes;
            UsagePercentage = usagePercentage;
        }

        /// <summary>
        /// Gets the unique identifier of the tenant.
        /// </summary>
        public string TenantId { get; }

        /// <summary>
        /// Gets the human-readable display name for the tenant.
        /// </summary>
        public string DisplayName { get; }

        /// <summary>
        /// Gets whether this tenant has administrative privileges.
        /// </summary>
        public bool IsAdmin { get; }

        /// <summary>
        /// Gets the maximum storage allowed for this tenant in bytes.
        /// </summary>
        public long StorageLimitBytes { get; }

        /// <summary>
        /// Gets the current storage usage in bytes.
        /// </summary>
        public long CurrentUsageBytes { get; }

        /// <summary>
        /// Gets the available storage space in bytes.
        /// </summary>
        public long AvailableSpaceBytes { get; }

        /// <summary>
        /// Gets the percentage of storage used (0-100).
        /// </summary>
        public double UsagePercentage { get; }
    }
}