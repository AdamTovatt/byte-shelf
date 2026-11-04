using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace ByteShelf.Server.Mcp
{
    /// <summary>
    /// This server's own REST API, reached at an address it listens on, which the MCP tools act through
    /// with the caller's key: every access check, the replica's read-only rule and the quota stay in
    /// the one place they already are, rather than a second path into the services.
    /// </summary>
    public sealed class SelfApi
    {
        /// <summary>The named HttpClient; a base address configured on it is used as it is (the tests set one).</summary>
        public const string HttpClientName = "byteshelf-self";

        private readonly IServer _server;
        private readonly IHttpClientFactory _httpClientFactory;

        public SelfApi(IServer server, IHttpClientFactory httpClientFactory)
        {
            _server = server;
            _httpClientFactory = httpClientFactory;
        }

        /// <exception cref="HttpRequestException">The server listens on no plain-HTTP address it can call itself at.</exception>
        public HttpClient CreateClient()
        {
            HttpClient client = _httpClientFactory.CreateClient(HttpClientName);
            client.BaseAddress ??= SelfAddressFor(_server.Features.Get<IServerAddressesFeature>()?.Addresses ?? Array.Empty<string>())
                ?? throw new HttpRequestException("This server listens on no plain-HTTP address it can reach itself at, so its MCP tools cannot run.");
            return client;
        }

        /// <summary>
        /// Where to reach a server listening on <paramref name="addresses"/> from the same machine: the
        /// first plain-HTTP one, its host as bound (loopback may not be listening beside a single
        /// interface's address), or the loopback one for a host that means every interface. HTTPS is
        /// passed over, since its certificate would not name the address called, and so is a Unix
        /// socket. Null when none will do.
        /// </summary>
        internal static Uri? SelfAddressFor(IEnumerable<string> addresses)
        {
            return addresses.Select(ToSelfAddress).FirstOrDefault(address => address != null);
        }

        private static Uri? ToSelfAddress(string address)
        {
            int schemeEnd = address.IndexOf("://", StringComparison.Ordinal);
            if (schemeEnd < 0)
            {
                return null;
            }

            string scheme = address[..schemeEnd];
            string rest = address[(schemeEnd + 3)..];
            if (rest.StartsWith("unix:", StringComparison.OrdinalIgnoreCase) || !scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            int pathStart = rest.IndexOf('/', StringComparison.Ordinal);
            string authority = pathStart < 0 ? rest : rest[..pathStart];
            int portStart = authority.LastIndexOf(':');
            bool hasPort = portStart > 0 && authority[portStart..].All(character => character == ':' || char.IsAsciiDigit(character));
            string host = hasPort ? authority[..portStart] : authority;
            string port = hasPort ? authority[portStart..] : string.Empty;

            string callableHost = host switch
            {
                "+" or "*" or "0.0.0.0" => "127.0.0.1",
                "[::]" => "[::1]",
                _ => host,
            };

            return Uri.TryCreate($"{scheme}://{callableHost}{port}/", UriKind.Absolute, out Uri? uri) ? uri : null;
        }
    }
}
