using System.Net;
using System.Text;
using ByteShelf.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace ByteShelf.Middleware
{
    /// <summary>
    /// Middleware that validates API keys in incoming HTTP requests.
    /// </summary>
    /// <remarks>
    /// This middleware intercepts HTTP requests and validates the presence and correctness
    /// of an API key in the "X-API-Key" header. It can be configured to require authentication
    /// for all endpoints or to skip authentication for specific paths (like health checks).
    /// When authentication fails, it returns a 401 Unauthorized response with a JSON error message.
    /// </remarks>
    public class ApiKeyAuthenticationMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly AuthenticationConfiguration _config;
        private const string ApiKeyHeaderName = "X-API-Key";

        /// <summary>
        /// Initializes a new instance of the <see cref="ApiKeyAuthenticationMiddleware"/> class.
        /// </summary>
        /// <param name="next">The next middleware in the request pipeline.</param>
        /// <param name="config">The authentication configuration containing the API key and settings.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="next"/> or <paramref name="config"/> is null.</exception>
        public ApiKeyAuthenticationMiddleware(
            RequestDelegate next,
            IOptions<AuthenticationConfiguration> config)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>
        /// Processes an HTTP request to validate API key authentication.
        /// </summary>
        /// <param name="context">The HTTP context for the current request.</param>
        /// <returns>A task that represents the asynchronous middleware operation.</returns>
        /// <remarks>
        /// This method:
        /// 1. Checks if the request path should skip authentication
        /// 2. Checks if authentication is required based on configuration
        /// 3. Validates the API key if authentication is required
        /// 4. Returns a 401 Unauthorized response if validation fails
        /// 5. Calls the next middleware if validation succeeds
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
            if (!_config.RequireAuthentication)
            {
                await _next(context);
                return;
            }

            // Validate API key
            if (!IsValidApiKey(context.Request))
            {
                context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                context.Response.ContentType = "application/json";

                string errorResponse = "{\"error\":\"Invalid or missing API key\",\"message\":\"Please provide a valid X-API-Key header\"}";
                byte[] errorBytes = Encoding.UTF8.GetBytes(errorResponse);
                await context.Response.Body.WriteAsync(errorBytes);
                return;
            }

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
        /// Additional paths can be added as needed.
        /// </remarks>
        private static bool ShouldSkipAuthentication(PathString path)
        {
            string pathValue = path.Value?.ToLowerInvariant() ?? string.Empty;
            
            return pathValue.StartsWith("/health") ||
                   pathValue.StartsWith("/swagger") ||
                   pathValue.StartsWith("/swagger-ui");
        }

        /// <summary>
        /// Validates the API key in the request headers.
        /// </summary>
        /// <param name="request">The HTTP request to validate.</param>
        /// <returns><c>true</c> if the API key is valid; otherwise, <c>false</c>.</returns>
        /// <remarks>
        /// This method checks for the presence of the "X-API-Key" header and validates
        /// that it matches the configured API key. The comparison is case-sensitive.
        /// Returns <c>false</c> if the header is missing, empty, or doesn't match.
        /// </remarks>
        private bool IsValidApiKey(HttpRequest request)
        {
            if (string.IsNullOrEmpty(_config.ApiKey))
                return false;

            if (!request.Headers.TryGetValue(ApiKeyHeaderName, out StringValues apiKeyValues))
                return false;

            string? providedApiKey = apiKeyValues.FirstOrDefault();
            return !string.IsNullOrEmpty(providedApiKey) && providedApiKey == _config.ApiKey;
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