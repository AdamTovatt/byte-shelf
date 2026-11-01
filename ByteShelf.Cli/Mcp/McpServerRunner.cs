using ByteShelf.Cli.Connection;
using ByteShelf.Tools;
using ByteShelf.Tools.Mcp;
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
        /// <summary>
        /// <see cref="ToolInstructions.Text"/>, and what only this server can do: work with the agent's
        /// own files, and reach every shelf the login can.
        /// </summary>
        internal const string Instructions = ToolInstructions.Text + """


            This server runs on your machine, signed in as the byteshelf CLI is, so list_shelves shows
            every shelf the login reaches. upload_file and download_file also take an absolute localPath, and
            upload_folder and download_directory move a whole local folder or shelf directory, keeping
            its structure.
            """;

        /// <summary>Runs the server until the client disconnects.</summary>
        public static async Task RunAsync(IClientSource clients, string[] args, CancellationToken cancellationToken)
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

            builder.Services.AddSingleton<IShelfClientSource>(clients);
            builder.Services.AddSingleton(ToolHost.Local);
            builder.Services.AddMcpServer(options => options.ServerInstructions = Instructions)
                .WithStdioServerTransport()
                .WithTools<ByteShelfMcpTools>();

            using IHost host = builder.Build();
            await host.RunAsync(cancellationToken);
        }
    }
}
