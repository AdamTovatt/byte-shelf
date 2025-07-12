namespace ByteShelfCommon
{
    /// <summary>
    /// Request model for creating a new subtenant.
    /// </summary>
    public class CreateSubTenantRequest
    {
        /// <summary>
        /// Gets or sets the display name for the subtenant.
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;
    }
} 