namespace ByteShelfCommon
{
    /// <summary>
    /// Response for subtenant creation.
    /// </summary>
    public class CreateSubTenantResponse
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CreateSubTenantResponse"/> class.
        /// </summary>
        /// <param name="tenantId">The tenant ID.</param>
        /// <param name="displayName">The display name.</param>
        /// <param name="message">The response message.</param>
        public CreateSubTenantResponse(string tenantId, string displayName, string message)
        {
            TenantId = tenantId;
            DisplayName = displayName;
            Message = message;
        }

        /// <summary>
        /// Gets the tenant ID.
        /// </summary>
        public string TenantId { get; }

        /// <summary>
        /// Gets the display name.
        /// </summary>
        public string DisplayName { get; }

        /// <summary>
        /// Gets the response message.
        /// </summary>
        public string Message { get; }
    }
}