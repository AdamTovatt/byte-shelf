using ByteShelf.Services;
using ByteShelfCommon.FileSystem;
using Microsoft.Extensions.Primitives;
using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace ByteShelf.Middleware
{
    /// <summary>
    /// Middleware that validates API keys in incoming HTTP requests using filesystem-based configuration.
    /// This middleware authenticates users (not tenants) and provides both user and tenant context.
    /// </summary>
    public class FileSystemApiKeyAuthenticationMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IFileSystemConfigurationService _configService;
        private const string ApiKeyHeaderName = "X-API-Key";
        private const string TenantIdHeaderName = "X-Tenant-ID";
        private const string UserIdHeaderName = "X-User-ID";

        // Thread-safe dictionary to track failed authentication attempts by IP address
        private static readonly ConcurrentDictionary<string, FailedAttemptInfo> _failedAttempts = new();

        /// <summary>
        /// Gets or sets a millisecond value that controls how much delay to add to a request with invalid api key each time one fails.
        /// Delay is added to prevent brute force attacks.
        /// </summary>
        public static int FailedAttemptMillisecondDelay { get; set; } = 500;

        public FileSystemApiKeyAuthenticationMiddleware(
            RequestDelegate next,
            IFileSystemConfigurationService configService)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        }

        /// <summary>
        /// Information about failed authentication attempts for a specific IP address.
        /// </summary>
        private class FailedAttemptInfo
        {
            public int FailedAttempts { get; set; }
            public DateTime LastFailedAttempt { get; set; }
        }

        /// <summary>
        /// Processes an HTTP request to validate API key authentication and identify the user and tenant.
        /// </summary>
        public async Task InvokeAsync(HttpContext context)
        {
            // Skip authentication for certain paths (like health checks)
            if (ShouldSkipAuthentication(context.Request.Path))
            {
                await _next(context);
                return;
            }

            // Get client IP address for rate limiting
            string clientIp = GetClientIpAddress(context);

            // Validate API key and get user/tenant
            if (!context.Request.Headers.TryGetValue(ApiKeyHeaderName, out StringValues apiKeyValues))
            {
                await HandleFailedAuthentication(clientIp);
                await WriteUnauthorizedResponse(context, "Missing API key", "Please provide a valid X-API-Key header");
                return;
            }

            string? providedApiKey = apiKeyValues.FirstOrDefault();
            if (string.IsNullOrEmpty(providedApiKey))
            {
                await HandleFailedAuthentication(clientIp);
                await WriteUnauthorizedResponse(context, "Empty API key", "Please provide a valid X-API-Key header");
                return;
            }

            // Authenticate using the filesystem configuration service
            var (user, tenant) = await _configService.AuthenticateAsync(providedApiKey);
            if (user == null || tenant == null)
            {
                await HandleFailedAuthentication(clientIp);
                await WriteUnauthorizedResponse(context, "Invalid API key", "The provided API key is not valid or the associated user/tenant is inactive");
                return;
            }

            // Add user and tenant information to the request context
            context.Items["UserId"] = user.UserId;
            context.Items["TenantId"] = user.TenantId;
            context.Items["IsAdmin"] = user.IsAdmin;
            context.Items["User"] = user;
            context.Items["Tenant"] = tenant;

            // Add headers for downstream services
            context.Request.Headers[TenantIdHeaderName] = user.TenantId;
            context.Request.Headers[UserIdHeaderName] = user.UserId;

            await _next(context);
        }

        /// <summary>
        /// Determines whether authentication should be skipped for the given request path.
        /// </summary>
        private static bool ShouldSkipAuthentication(PathString path)
        {
            string pathValue = path.Value?.ToLowerInvariant() ?? string.Empty;

            return pathValue.StartsWith("/health") ||
                   pathValue.StartsWith("/swagger") ||
                   pathValue.StartsWith("/swagger-ui") ||
                   pathValue == "/" ||
                   pathValue == "/styles.css" ||
                   pathValue == "/script.js" ||
                   pathValue == "/ping" ||
                   pathValue == "/favicon.ico";
        }

        /// <summary>
        /// Gets the client IP address from the request context.
        /// </summary>
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
        /// Writes an unauthorized response to the HTTP context.
        /// </summary>
        private static async Task WriteUnauthorizedResponse(HttpContext context, string error, string message)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            context.Response.ContentType = "application/json";

            string errorResponse = $"{{\"error\":\"{error}\",\"message\":\"{message}\"}}";
            byte[] errorBytes = Encoding.UTF8.GetBytes(errorResponse);
            await context.Response.Body.WriteAsync(errorBytes);
        }
    }

    /// <summary>
    /// Extension methods for registering the FileSystemApiKeyAuthenticationMiddleware in the application pipeline.
    /// </summary>
    public static class FileSystemApiKeyAuthenticationMiddlewareExtensions
    {
        /// <summary>
        /// Adds filesystem-based API key authentication middleware to the application's request pipeline.
        /// </summary>
        public static IApplicationBuilder UseFileSystemApiKeyAuthentication(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<FileSystemApiKeyAuthenticationMiddleware>();
        }
    }
}