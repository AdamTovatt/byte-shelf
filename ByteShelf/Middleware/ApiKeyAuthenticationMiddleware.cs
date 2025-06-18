using System.Net;
using System.Text;
using ByteShelf.Configuration;
using Microsoft.Extensions.Options;

namespace ByteShelf.Middleware
{
    public class ApiKeyAuthenticationMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly AuthenticationConfiguration _config;
        private const string ApiKeyHeaderName = "X-API-Key";

        public ApiKeyAuthenticationMiddleware(
            RequestDelegate next,
            IOptions<AuthenticationConfiguration> config)
        {
            _next = next;
            _config = config.Value;
        }

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

        private bool ShouldSkipAuthentication(PathString path)
        {
            // Skip authentication for health checks and other system endpoints
            string pathValue = path.Value?.ToLowerInvariant() ?? string.Empty;
            return pathValue.StartsWith("/health") || 
                   pathValue.StartsWith("/metrics") ||
                   pathValue == "/";
        }

        private bool IsValidApiKey(HttpRequest request)
        {
            // Check if API key header is present
            if (!request.Headers.TryGetValue(ApiKeyHeaderName, out var apiKeyHeader))
            {
                return false;
            }

            string providedApiKey = apiKeyHeader.FirstOrDefault() ?? string.Empty;
            
            // Validate against configured API key
            return !string.IsNullOrEmpty(providedApiKey) && 
                   providedApiKey.Equals(_config.ApiKey, StringComparison.Ordinal);
        }
    }

    // Extension method for easy registration
    public static class ApiKeyAuthenticationMiddlewareExtensions
    {
        public static IApplicationBuilder UseApiKeyAuthentication(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<ApiKeyAuthenticationMiddleware>();
        }
    }
}