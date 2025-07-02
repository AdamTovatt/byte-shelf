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
    }
}