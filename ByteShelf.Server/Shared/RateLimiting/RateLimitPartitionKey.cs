using EasyReasy.Auth;

namespace ByteShelf.Server.Shared.RateLimiting
{
    /// <summary>
    /// The single definition of "who is the caller" for rate limiting: the signed-in user or the shelf
    /// key, falling back to the remote IP for anonymous requests. Shared by the global limiter, every
    /// named policy and the live-update stream limit, so all of them count the same caller the same way.
    /// Behind a reverse proxy the remote IP is the client's only when the proxy is listed in
    /// <c>BYTESHELF_TRUSTED_PROXIES</c>, which the forwarded-headers middleware reads.
    /// </summary>
    public static class RateLimitPartitionKey
    {
        public static string Resolve(HttpContext httpContext)
        {
            // A session token carries no name, only its user's id; a key's identity is named after the key.
            string? userId = httpContext.GetUserId();
            if (userId != null)
            {
                return $"user:{userId}";
            }

            return httpContext.User.Identity?.Name
                ?? httpContext.Connection.RemoteIpAddress?.ToString()
                ?? "anonymous";
        }
    }
}
