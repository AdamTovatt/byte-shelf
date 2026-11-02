using ByteShelf.Tools;
using ByteShelf.Tools.Mcp;
using ByteShelf.Server.Access.Auth;

namespace ByteShelf.Server.Mcp
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>The route the MCP endpoint answers at.</summary>
        public const string Route = "/mcp";

        /// <summary>The policy <see cref="Route"/> requires: a shelf key, and nothing else.</summary>
        public const string McpCallerPolicy = "McpCaller";

        /// <summary>
        /// The MCP tools over Streamable HTTP, statelessly, for shelf keys: the same tools as
        /// <c>byteshelf --mcp</c>, acting through this server's own API with the caller's key.
        /// </summary>
        public static IServiceCollection AddMcpServices(this IServiceCollection services)
        {
            services.AddHttpContextAccessor();
            services.AddHttpClient(SelfApi.HttpClientName);
            services.AddSingleton<SelfApi>();
            services.AddSingleton(ToolHost.Server);
            services.AddScoped<IShelfClientSource, CallerKeyClientSource>();

            services.AddAuthorization(options => options.AddPolicy(McpCallerPolicy, policy => policy
                .AddAuthenticationSchemes(ShelfKeyAuthentication.Scheme)
                .RequireAuthenticatedUser()));

            services.AddMcpServer()
                .WithHttpTransport(options =>
                {
                    options.Stateless = true;
                    options.ConfigureSessionOptions = McpCallerOptions.ConfigureAsync;
                })
                .WithByteShelfTools();

            return services;
        }

        /// <summary>
        /// Maps <see cref="Route"/>, open to shelf keys only, and <c>/llms.txt</c>, open to anyone: what
        /// an agent reads to learn how to use this server (Mcp/llms.txt, built in).
        /// </summary>
        public static void MapAgentEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapMcp(Route).RequireAuthorization(McpCallerPolicy);

            string llmsTxt = ReadLlmsTxt();
            endpoints.MapGet("/llms.txt", () => Results.Text(llmsTxt, "text/plain; charset=utf-8"))
                .AllowAnonymous()
                .ExcludeFromDescription();
        }

        private static string ReadLlmsTxt()
        {
            using Stream stream = typeof(ServiceCollectionExtensions).Assembly.GetManifestResourceStream("llms.txt")
                ?? throw new InvalidOperationException("llms.txt is not built into the server.");
            using StreamReader reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
