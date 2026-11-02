using ByteShelf.Tools;
using ByteShelf.Server.Access.Auth;
using ByteShelfClient;
using ByteShelfClient.Http;

namespace ByteShelf.Server.Mcp
{
    /// <summary>
    /// The client an MCP tool call acts through on the server: this server's API, with the key the call
    /// presented. It reaches the API at an address of its own, so a link the API answers relative (no
    /// <c>APP_BASE_URL</c>, see <see cref="Shared.PublicAddress"/>) is put on the address the agent
    /// called <c>/mcp</c> at instead (its <c>Host</c> header as sent, which a proxy in front passes on),
    /// where the person the agent hands it to can open it too.
    /// </summary>
    internal sealed class CallerKeyClientSource : IShelfClientSource
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly SelfApi _selfApi;

        public CallerKeyClientSource(IHttpContextAccessor httpContextAccessor, SelfApi selfApi)
        {
            _httpContextAccessor = httpContextAccessor;
            _selfApi = selfApi;
        }

        public Task<IByteShelfClient> OpenAsync(CancellationToken cancellationToken)
        {
            // /mcp is reached only with a key that authenticated, so the request carries one.
            HttpContext httpContext = _httpContextAccessor.HttpContext
                ?? throw new InvalidOperationException("An MCP tool ran outside the request that called it.");
            string secret = ShelfKeyAuthenticationHandler.SecretOf(httpContext)
                ?? throw new UsageException("Send a shelf key, as the bearer token or in X-Api-Key.");

            ByteShelfHttpClientOptions options = new ByteShelfHttpClientOptions { LinkBaseAddress = CalledAt(httpContext.Request) };
            return Task.FromResult<IByteShelfClient>(new ByteShelfHttpClient(_selfApi.CreateClient(), new ShelfKeyCredentials(secret), options));
        }

        /// <summary>The server's root as the caller reached it, ending in <c>/</c>.</summary>
        internal static Uri CalledAt(HttpRequest request)
        {
            return new Uri($"{request.Scheme}://{request.Host.ToUriComponent()}{request.PathBase.ToUriComponent()}/");
        }
    }
}
