using ByteShelf.Configuration;
using ByteShelf.Services;
using ByteShelfCommon;
using Microsoft.Extensions.Primitives;
using System.Net;
using System.Text;
using System.Collections.Concurrent;

namespace ByteShelf.Middleware
{
    /// <summary>
    /// Middleware that validates API keys in incoming HTTP requests and identifies tenants.
    /// </summary>
    /// <remarks>
    /// This middleware intercepts HTTP requests and validates the presence and correctness
    /// of an API key in the "X-API-Key" header. It identifies the tenant based on the API key
    /// and adds the tenant ID to the request context for use by downstream components.
    /// It can be configured to require authentication for all endpoints or to skip authentication
    /// for specific paths (like health checks). When authentication fails, it returns a 401
    /// Unauthorized response with a JSON error message.
    /// </remarks>
    public class ApiKeyAuthenticationMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ITenantConfigurationService _configService;
        private const string ApiKeyHeaderName = "X-API-Key";
        private const string TenantIdHeaderName = "X-Tenant-ID";

        // Thread-safe dictionary to track failed authentication attempts by IP address
        private static readonly ConcurrentDictionary<string, FailedAttemptInfo> _failedAttempts = new ConcurrentDictionary<string, FailedAttemptInfo>();

        /// <summary>
        /// Gets or sets a millisecond value that controls how much delay to add to a request with invalid api key each time one fails.
        /// Delay is added to prevent brute force attacks.
        /// </summary>
        public static int FailedAttemptMillisecondDelay { get; set; } = 500;

        /// <summary>
        /// Initializes a new instance of the <see cref="ApiKeyAuthenticationMiddleware"/> class.
        /// </summary>
        /// <param name="next">The next middleware in the request pipeline.</param>
        /// <param name="configService">The tenant configuration service.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="next"/> or <paramref name="configService"/> is null.</exception>
        public ApiKeyAuthenticationMiddleware(
            RequestDelegate next,
            ITenantConfigurationService configService)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        }

        /// <summary>
        /// Information about failed authentication attempts for a specific IP address.
        /// </summary>
        private class FailedAttemptInfo
        {
            /// <summary>
            /// Gets or sets the number of failed attempts.
            /// </summary>
            public int FailedAttempts { get; set; }

            /// <summary>
            /// Gets or sets the timestamp of the last failed attempt.
            /// </summary>
            public DateTime LastFailedAttempt { get; set; }
        }

        /// <summary>
        /// Processes an HTTP request to validate API key authentication and identify the tenant.
        /// </summary>
        /// <param name="context">The HTTP context for the current request.</param>
        /// <returns>A task that represents the asynchronous middleware operation.</returns>
        /// <remarks>
        /// This method:
        /// 1. Checks if the request path should skip authentication
        /// 2. Checks if authentication is required based on configuration
        /// 3. Validates the API key and identifies the tenant if authentication is required
        /// 4. Adds the tenant ID to the request context for downstream components
        /// 5. Returns a 401 Unauthorized response if validation fails
        /// 6. Calls the next middleware if validation succeeds
        /// 7. Implements rate limiting for failed authentication attempts
        /// </remarks>
        public async Task InvokeAsync(HttpContext context)
        {
            // Skip authentication for certain paths (like health checks)
            if (ShouldSkipAuthentication(context.Request.Path))
            {
                await _next(context);
                return;
            }

            // Check if authentication is required
            TenantConfiguration config = _configService.GetConfiguration();
            if (!config.RequireAuthentication)
            {
                await _next(context);
                return;
            }

            // Get client IP address for rate limiting
            string clientIp = GetClientIpAddress(context);

            // Validate API key and get tenant ID
            string? tenantId = GetTenantIdFromApiKey(context.Request);
            if (tenantId == null)
            {
                // Record failed attempt and apply rate limiting
                await HandleFailedAuthentication(clientIp);

                context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                context.Response.ContentType = "application/json";

                string errorResponse = "{\"error\":\"Invalid or missing API key\",\"message\":\"Please provide a valid X-API-Key header\"}";
                byte[] errorBytes = Encoding.UTF8.GetBytes(errorResponse);
                await context.Response.Body.WriteAsync(errorBytes);
                return;
            }

            // Get tenant info (could be root tenant or subtenant)
            TenantInfo? tenantInfo = GetTenantInfo(tenantId, config);
            if (tenantInfo == null)
            {
                context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                context.Response.ContentType = "application/json";
                string errorResponse = "{\"error\":\"Tenant not found\",\"message\":\"The authenticated tenant was not found in configuration\"}";
                byte[] errorBytes = Encoding.UTF8.GetBytes(errorResponse);
                await context.Response.Body.WriteAsync(errorBytes);
                return;
            }

            // Add tenant ID and admin status to the request context for downstream components
            context.Items["TenantId"] = tenantId;
            context.Items["IsAdmin"] = tenantInfo.IsAdmin;
            context.Request.Headers[TenantIdHeaderName] = tenantId;

            await _next(context);
        }

        /// <summary>
        /// Determines whether authentication should be skipped for the given request path.
        /// </summary>
        /// <param name="path">The request path to check.</param>
        /// <returns><c>true</c> if authentication should be skipped; otherwise, <c>false</c>.</returns>
        /// <remarks>
        /// Currently skips authentication for:
        /// - Health check endpoints ("/health", "/healthz")
        /// - Swagger/OpenAPI documentation endpoints ("/swagger", "/swagger-ui")
        /// - Frontend resources ("/", "/styles.css", "/script.js", "/ping")
        /// Additional paths can be added as needed.
        /// </remarks>
        private static bool ShouldSkipAuthentication(PathString path)
        {
            string pathValue = path.Value?.ToLowerInvariant() ?? string.Empty;

            return pathValue.StartsWith("/health") ||
                   pathValue.StartsWith("/swagger") ||
                   pathValue.StartsWith("/swagger-ui") ||
                   pathValue == "/" ||
                   pathValue == "/styles.css" ||
                   pathValue == "/script.js" ||
                   pathValue == "/ping";
        }

        /// <summary>
        /// Gets the client IP address from the request context.
        /// </summary>
        /// <param name="context">The HTTP context.</param>
        /// <returns>The client IP address.</returns>
        /// <remarks>
        /// This method checks for forwarded headers (X-Forwarded-For, X-Real-IP) to handle
        /// requests that come through proxies or load balancers, falling back to the direct
        /// connection IP address.
        /// </remarks>
        private static string GetClientIpAddress(HttpContext context)
        {
            // Check for forwarded headers first
            string? forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrEmpty(forwardedFor))
            {
                // X-Forwarded-For can contain multiple IPs, take the first one
                return forwardedFor.Split(',')[0].Trim();
            }

            string? realIp = context.Request.Headers["X-Real-IP"].FirstOrDefault();
            if (!string.IsNullOrEmpty(realIp))
            {
                return realIp;
            }

            // Fall back to the direct connection IP
            return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        }

        /// <summary>
        /// Handles a failed authentication attempt by recording it and applying rate limiting.
        /// </summary>
        /// <param name="clientIp">The client IP address.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        /// <remarks>
        /// This method records the failed attempt and applies a progressive delay based on
        /// the number of failed attempts. The delay is 500ms multiplied by the number of
        /// failed attempts, providing an exponential backoff effect.
        /// </remarks>
        private static async Task HandleFailedAuthentication(string clientIp)
        {
            // Get or create failed attempt info for this IP
            FailedAttemptInfo failedInfo = _failedAttempts.GetOrAdd(clientIp, _ => new FailedAttemptInfo());

            // Update failed attempt count and timestamp
            failedInfo.FailedAttempts++;
            failedInfo.LastFailedAttempt = DateTime.UtcNow;

            // Apply progressive delay: 500ms * number of failed attempts
            int delayMs = Math.Max(1, (failedInfo.FailedAttempts - 1) * FailedAttemptMillisecondDelay);
            await Task.Delay(delayMs);
        }

        /// <summary>
        /// Validates the API key in the request headers and returns the associated tenant ID.
        /// </summary>
        /// <param name="request">The HTTP request to validate.</param>
        /// <returns>The tenant ID if the API key is valid; otherwise, <c>null</c>.</returns>
        /// <remarks>
        /// This method checks for the presence of the "X-API-Key" header and validates
        /// that it matches one of the configured tenant API keys, including subtenants.
        /// The comparison is case-sensitive. Returns the tenant ID if the header is present
        /// and matches a configured key, or <c>null</c> if the header is missing, empty,
        /// or doesn't match any tenant.
        /// </remarks>
        private string? GetTenantIdFromApiKey(HttpRequest request)
        {
            if (!request.Headers.TryGetValue(ApiKeyHeaderName, out StringValues apiKeyValues))
                return null;

            string? providedApiKey = apiKeyValues.FirstOrDefault();
            if (string.IsNullOrEmpty(providedApiKey))
                return null;

            // Find the tenant with the matching API key (including subtenants)
            TenantConfiguration config = _configService.GetConfiguration();
            
            // Check root tenants first
            foreach (KeyValuePair<string, TenantInfo> tenant in config.Tenants)
            {
                if (tenant.Value.ApiKey == providedApiKey)
                {
                    return tenant.Key;
                }
            }

            // Search in subtenants recursively
            foreach (TenantInfo rootTenant in config.Tenants.Values)
            {
                string? foundTenantId = FindTenantByApiKeyInSubTenants(providedApiKey, rootTenant);
                if (foundTenantId != null)
                {
                    return foundTenantId;
                }
            }

            return null;
        }

        /// <summary>
        /// Recursively searches for a tenant with the specified API key in subtenants.
        /// </summary>
        /// <param name="apiKey">The API key to search for.</param>
        /// <param name="tenant">The tenant to search in.</param>
        /// <returns>The tenant ID if found, or null if not found.</returns>
        private string? FindTenantByApiKeyInSubTenants(string apiKey, TenantInfo tenant)
        {
            foreach (KeyValuePair<string, TenantInfo> subTenant in tenant.SubTenants)
            {
                if (subTenant.Value.ApiKey == apiKey)
                {
                    return subTenant.Key;
                }

                // Recursively search in this subtenant's subtenants
                string? found = FindTenantByApiKeyInSubTenants(apiKey, subTenant.Value);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>
        /// Gets tenant information by tenant ID, searching through root tenants and subtenants.
        /// </summary>
        /// <param name="tenantId">The tenant ID to find.</param>
        /// <param name="config">The tenant configuration.</param>
        /// <returns>The tenant information, or null if not found.</returns>
        private TenantInfo? GetTenantInfo(string tenantId, TenantConfiguration config)
        {
            // Check root tenants first
            if (config.Tenants.TryGetValue(tenantId, out TenantInfo? tenant))
            {
                return tenant;
            }

            // Search in subtenants recursively
            foreach (TenantInfo rootTenant in config.Tenants.Values)
            {
                TenantInfo? found = FindTenantInSubTenants(tenantId, rootTenant);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>
        /// Recursively searches for a tenant in subtenants.
        /// </summary>
        /// <param name="tenantId">The tenant ID to find.</param>
        /// <param name="tenant">The tenant to search in.</param>
        /// <returns>The found tenant, or null if not found.</returns>
        private TenantInfo? FindTenantInSubTenants(string tenantId, TenantInfo tenant)
        {
            if (tenant.SubTenants.TryGetValue(tenantId, out TenantInfo? found))
            {
                return found;
            }

            foreach (TenantInfo subTenant in tenant.SubTenants.Values)
            {
                TenantInfo? result = FindTenantInSubTenants(tenantId, subTenant);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Extension methods for registering the <see cref="ApiKeyAuthenticationMiddleware"/> in the application pipeline.
    /// </summary>
    public static class ApiKeyAuthenticationMiddlewareExtensions
    {
        /// <summary>
        /// Adds API key authentication middleware to the application's request pipeline.
        /// </summary>
        /// <param name="builder">The application builder to configure.</param>
        /// <returns>The application builder with the middleware configured.</returns>
        /// <remarks>
        /// This extension method registers the <see cref="ApiKeyAuthenticationMiddleware"/> so that all incoming requests
        /// are validated for API key authentication, except for excluded endpoints (such as health checks and Swagger docs).
        /// </remarks>
        public static IApplicationBuilder UseApiKeyAuthentication(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<ApiKeyAuthenticationMiddleware>();
        }
    }
}