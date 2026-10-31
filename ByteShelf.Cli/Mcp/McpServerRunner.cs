using ByteShelf.Cli.Connection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ByteShelf.Cli.Mcp
{
    /// <summary>
    /// Serves <see cref="ByteShelfMcpTools"/> over stdio for <c>byteshelf --mcp</c>. Standard output is
    /// the protocol, so every log line goes to standard error.
    /// </summary>
    internal static class McpServerRunner
    {
        /// <summary>Runs the server until the client disconnects.</summary>
        public static async Task RunAsync(IClientSource clients, string[] args, CancellationToken cancellationToken)
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

            builder.Services.AddSingleton(clients);
            builder.Services.AddMcpServer().WithStdioServerTransport().WithTools<ByteShelfMcpTools>();

            using IHost host = builder.Build();
            await host.RunAsync(cancellationToken);
        }
    }
}
