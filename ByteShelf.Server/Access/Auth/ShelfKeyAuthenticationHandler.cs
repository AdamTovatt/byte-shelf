using System.Security.Claims;
using System.Text.Encodings.Web;
using ByteShelf.Server.Access.Models;
using ByteShelf.Server.Access.Services;
using ByteShelfCommon;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace ByteShelf.Server.Access.Auth
{
    /// <summary>
    /// Authenticates a request presenting a shelf key, in <see cref="ShelfKeys.HeaderName"/> or as a
    /// bearer token (what an MCP client sends), looking the key up on every request, so a deleted key
    /// stops working at once. A bearer token is a key only when it starts with
    /// <see cref="ShelfKeys.SecretPrefix"/>, which no session token does. A request without a key is left
    /// to the other schemes.
    /// </summary>
    public class ShelfKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        private const string BearerPrefix = "Bearer ";

        private static readonly object KeyItem = new object();

        private readonly IShelfKeyService _keyService;

        public ShelfKeyAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            IShelfKeyService keyService)
            : base(options, logger, encoder)
        {
            _keyService = keyService;
        }

        /// <summary>Whether the request carries a key at all, valid or not.</summary>
        public static bool PresentsKey(HttpContext httpContext)
        {
            return httpContext.Request.Headers.ContainsKey(ShelfKeys.HeaderName) || BearerKeyOf(httpContext) != null;
        }

        /// <summary>Whether the request's bearer token is a shelf key rather than a session token.</summary>
        public static bool SendsKeyAsBearer(HttpContext httpContext)
        {
            return BearerKeyOf(httpContext) != null;
        }

        /// <summary>
        /// The key's secret as the request presents it, or null when it presents none, or more than one
        /// value, or a header and a bearer key both.
        /// </summary>
        public static string? SecretOf(HttpContext httpContext)
        {
            StringValues header = httpContext.Request.Headers[ShelfKeys.HeaderName];
            string? bearer = BearerKeyOf(httpContext);
            if (header.Count == 0)
            {
                return bearer;
            }

            return header.Count == 1 && bearer == null && !string.IsNullOrEmpty(header[0]) ? header[0] : null;
        }

        private static string? BearerKeyOf(HttpContext httpContext)
        {
            StringValues authorization = httpContext.Request.Headers.Authorization;
            if (authorization.Count != 1 || authorization[0] is not string value || !value.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string token = value[BearerPrefix.Length..].Trim();
            return token.StartsWith(ShelfKeys.SecretPrefix, StringComparison.Ordinal) ? token : null;
        }

        /// <summary>The key this request was authenticated with, or null.</summary>
        public static ShelfKey? GetKey(HttpContext httpContext)
        {
            return httpContext.Items[KeyItem] as ShelfKey;
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!PresentsKey(Context))
            {
                return AuthenticateResult.NoResult();
            }

            string? secret = SecretOf(Context);
            if (secret == null)
            {
                return AuthenticateResult.Fail($"Send the key once: in one {ShelfKeys.HeaderName} header or as the bearer token, not both.");
            }

            ShelfKey? key = await _keyService.FindBySecretAsync(secret);
            if (key == null)
            {
                return AuthenticateResult.Fail("The shelf key is not valid.");
            }

            Context.Items[KeyItem] = key;

            ClaimsIdentity identity = new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Name, $"shelf-key:{key.Id:D}") },
                Scheme.Name);

            return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
        }
    }
}
