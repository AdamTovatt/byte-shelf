using ByteShelfCommon;

namespace ByteShelf.Configuration
{
    /// <summary>
    /// Configuration settings for multi-tenant API key authentication.
    /// </summary>
    /// <remarks>
    /// This class contains the settings used by the <see cref="Middleware.ApiKeyAuthenticationMiddleware"/>
    /// to validate API keys and identify tenants in incoming requests. The settings can be configured through
    /// the "Tenants" section in appsettings.json or via environment variables.
    /// </remarks>
    public class TenantConfiguration
    {
        /// <summary>
        /// Gets or sets the dictionary of tenants, keyed by tenant ID.
        /// </summary>
        /// <remarks>
        /// Each tenant has an API key and storage limit. The API key is used for authentication,
        /// and the storage limit is enforced to prevent unlimited data storage.
        /// </remarks>
        public Dictionary<string, TenantInfo> Tenants { get; set; } = new Dictionary<string, TenantInfo>();

        /// <summary>
        /// Gets or sets a value indicating whether authentication is required.
        /// </summary>
        /// <remarks>
        /// When set to <c>true</c>, all requests (except excluded endpoints) must include a valid API key.
        /// When set to <c>false</c>, authentication is disabled and all requests are allowed.
        /// Defaults to <c>true</c> for security. Set to <c>false</c> only for development or testing.
        /// </remarks>
        public bool RequireAuthentication { get; set; } = true;
    }
}