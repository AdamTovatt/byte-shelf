using EasyReasy.Auth;

namespace ByteShelf.Server.Shared.Auth
{
    /// <summary>The auth settings both the full server and the schema-only boot use, written once.</summary>
    public static class AuthSetup
    {
        /// <summary>The issuer stamped on, and required of, every JWT.</summary>
        public const string JwtIssuer = "byteshelf";

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
