namespace ByteShelfCommon
{
    /// <summary>
    /// Request model for creating a new tenant.
    /// </summary>
    public class CreateTenantRequest
    {
        /// <summary>
        /// Gets or sets the tenant ID.
        /// </summary>
        public string TenantId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the API key for the tenant.
        /// </summary>
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the display name for the tenant.
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the storage limit in bytes.
        /// </summary>
        public long StorageLimitBytes { get; set; }

        /// <summary>
        /// Gets or sets whether the tenant has administrative privileges.
        /// </summary>
        public bool IsAdmin { get; set; } = false;
    }
}