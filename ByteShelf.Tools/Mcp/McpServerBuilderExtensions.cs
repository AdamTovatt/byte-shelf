using Microsoft.Extensions.DependencyInjection;

namespace ByteShelf.Tools.Mcp
{
    internal static class McpServerBuilderExtensions
    {
        /// <summary>
        /// The tools and the filters they are served with, one registration for both servers
        /// (<c>byteshelf --mcp</c> and the server's <c>/mcp</c>), so neither can serve them without the other's
        /// filters.
        /// </summary>
        public static IMcpServerBuilder WithByteShelfTools(this IMcpServerBuilder builder)
        {
            return builder
                .WithTools<ByteShelfMcpTools>()
                .WithRequestFilters(filters => filters.AddCallToolFilter(RequiredArguments.Filter));
        }
    }
}
