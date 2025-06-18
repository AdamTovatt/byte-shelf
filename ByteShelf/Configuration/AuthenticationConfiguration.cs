namespace ByteShelf.Configuration
{
    /// <summary>
    /// Configuration settings for API key authentication.
    /// </summary>
    /// <remarks>
    /// This class contains the settings used by the <see cref="Middleware.ApiKeyAuthenticationMiddleware"/>
    /// to validate API keys in incoming requests. The settings can be configured through
    /// the "Authentication" section in appsettings.json or via environment variables.
    /// </remarks>
    public class AuthenticationConfiguration
    {
        /// <summary>
        /// Gets or sets the API key that clients must provide for authentication.
        /// </summary>
        /// <remarks>
        /// This is the secret key that clients include in the "X-API-Key" header.
        /// For security, this should be a strong, randomly generated key.
        /// In production, consider using environment variables instead of storing this in configuration files.
        /// </remarks>
        public string ApiKey { get; set; } = string.Empty;
        
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