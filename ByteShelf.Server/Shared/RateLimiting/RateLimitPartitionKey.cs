namespace ByteShelf.Server.Shared.RateLimiting
{
    /// <summary>
    /// The single definition of "who is the caller" for rate limiting: the authenticated user
    /// name, falling back to the remote IP for anonymous requests. Shared by the global limiter
    /// and every named policy so all tiers count the same caller the same way. Behind a reverse
    /// proxy the remote IP is the client's only when the proxy is listed in
    /// <c>BYTESHELF_TRUSTED_PROXIES</c>, which the forwarded-headers middleware reads.
    /// </summary>
    public static class RateLimitPartitionKey
    {
        public static string Resolve(HttpContext httpContext)
        {
            return httpContext.User.Identity?.Name
                ?? httpContext.Connection.RemoteIpAddress?.ToString()
                ?? "anonymous";
        }
    }
}
