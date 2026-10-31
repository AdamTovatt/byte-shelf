using System.Security.Claims;
using System.Text.Encodings.Web;
using ByteShelf.Server.Access.Models;
using ByteShelf.Server.Access.Services;
using ByteShelfCommon;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace ByteShelf.Server.Access.Auth
{
    /// <summary>
    /// Authenticates a request carrying <see cref="ShelfKeys.HeaderName"/>, looking the key up on every
    /// request, so a deleted key stops working at once. A request without the header is left to the
    /// other schemes.
    /// </summary>
    public class ShelfKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
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
            return httpContext.Request.Headers.ContainsKey(ShelfKeys.HeaderName);
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

            string? secret = Request.Headers[ShelfKeys.HeaderName].Count == 1 ? Request.Headers[ShelfKeys.HeaderName][0] : null;
            if (string.IsNullOrEmpty(secret))
            {
                return AuthenticateResult.Fail($"Send exactly one {ShelfKeys.HeaderName} header.");
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
