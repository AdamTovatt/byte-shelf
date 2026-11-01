using ByteShelf.Tools;
using ByteShelf.Server.Access.Auth;
using ByteShelfClient;
using ByteShelfClient.Http;

namespace ByteShelf.Server.Mcp
{
    /// <summary>The client an MCP tool call acts through on the server: this server's API, with the key the call presented.</summary>
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

            return Task.FromResult<IByteShelfClient>(new ByteShelfHttpClient(_selfApi.CreateClient(), new ShelfKeyCredentials(secret)));
        }
    }
}
