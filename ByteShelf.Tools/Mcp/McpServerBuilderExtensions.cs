using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace ByteShelf.Tools.Mcp
{
    internal static class McpServerBuilderExtensions
    {
        /// <summary>
        /// The tools and the filters they are served with, one registration for both servers
        /// (<c>byteshelf --mcp</c> and the server's <c>/mcp</c>), so neither can serve them without the other's
        /// filters.
        /// </summary>
        /// <param name="builder">The server.</param>
        /// <param name="host">What the tools can do where they run.</param>
        public static IMcpServerBuilder WithByteShelfTools(this IMcpServerBuilder builder, Func<IServiceProvider, ToolHost> host)
        {
            builder.Services.AddSingleton(host);
            builder.Services.AddOptions<McpServerOptions>().PostConfigure<ToolHost>(DescribeOn);
            return builder
                .WithTools<ByteShelfMcpTools>()
                .WithRequestFilters(filters => filters.AddCallToolFilter(RequiredArguments.Filter));
        }

        /// <summary>Says in download_file's description what it does on this host, which an attribute's constant cannot.</summary>
        private static void DescribeOn(McpServerOptions options, ToolHost host)
        {
            foreach (McpServerTool tool in (options.ToolCollection ?? []).Where(tool => tool.ProtocolTool.Name == ByteShelfMcpTools.DownloadFileName))
            {
                tool.ProtocolTool.Description = $"{ByteShelfMcpTools.DownloadFileDescription} {ToolInstructions.OtherFilesOn(host)}";
            }
        }
    }
}
