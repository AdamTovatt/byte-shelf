using EasyReasy.Auth;

namespace ByteShelf.Server.Shared.Auth
{
    /// <summary>The auth settings both the full server and the schema-only boot use, written once.</summary>
    public static class AuthSetup
    {
        /// <summary>The issuer stamped on, and required of, every JWT.</summary>
        public const string JwtIssuer = "byteshelf";

        /// <summary>
        /// The one path prefix where a signed-in browser's token may arrive in a cookie (EasyReasy.Auth's
        /// <c>CookieTokenPaths</c>), for a file opened or saved by plain navigation, which cannot send a
        /// header. Only <c>GET</c> and <c>HEAD</c> read it, and nothing under it may change anything.
        /// The built-in login, refresh and logout endpoints set and clear the cookie.
        /// </summary>
        public const string CookieTokenPath = "/api/open";

        /// <summary>The auth options both the full server and the schema-only boot use.</summary>
        /// <remarks>
        /// With <c>CookieTokenPaths</c> set, EasyReasy.Auth wraps the session scheme's
        /// <c>JwtBearerEvents</c> in a <c>PostConfigure</c> of its own, and a handler set on the wrapper
        /// after that never runs. So every handler ByteShelf adds to those events is added in a
        /// <c>Configure</c> of <c>JwtBearerOptions</c>, never a <c>PostConfigure</c>.
        /// </remarks>
        public static void Configure(EasyReasyAuthOptions options)
        {
            options.Issuer = JwtIssuer;
            options.CookieTokenPaths = [CookieTokenPath];
        }

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
