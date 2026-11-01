using EasyReasy.Auth;

namespace ByteShelf.Server.Shared.Auth
{
    /// <summary>The auth settings both the full server and the schema-only boot use, written once.</summary>
    public static class AuthSetup
    {
        /// <summary>The issuer stamped on, and required of, every JWT.</summary>
        public const string JwtIssuer = "byteshelf";

        /// <summary>
        /// Whether the request was routed to signing in or renewing a session, which the rate limiter holds
        /// to a strict tier. By the route matched rather than the path sent, so no spelling of the path
        /// escapes it.
        /// </summary>
        public static bool IsSignIn(HttpContext httpContext)
        {
            string? route = (httpContext.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;
            return route is "/api/auth/login" or "/api/auth/refresh";
        }

        /// <summary>Maps <c>/api/auth/login</c>, <c>/refresh</c> and <c>/logout</c>; programs authenticate with shelf keys instead.</summary>
        public static void MapAuthEndpoints(WebApplication app)
        {
            app.AddAuthEndpoints(
                allowUsernamePassword: true,
                allowApiKeys: false,
                allowRefresh: true,
                allowLogout: true);
        }
    }
}
