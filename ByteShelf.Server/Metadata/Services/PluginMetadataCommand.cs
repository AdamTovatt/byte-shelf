using ByteShelf.Server.Replication;
using ByteShelf.Server.Replication.Services;
using ByteShelf.Server.Shared.Root;
using EasyReasy.Database;

namespace ByteShelf.Server.Metadata.Services
{
    /// <summary>
    /// <c>ByteShelf.Server --forget-plugin &lt;id&gt;</c>: deletes everything a plugin keeps about the files on
    /// the root at <c>BYTESHELF_ROOT</c>, for one that is uninstalled for good, from the server's own machine,
    /// with the server running or stopped. Uninstalling alone keeps it, for the plugin to come back to. A
    /// replica refuses, and loses it once its primary does. Answers the process's exit code, one of
    /// <see cref="RootCommand"/>'s.
    /// </summary>
    public static class PluginMetadataCommand
    {
        /// <summary>The flags that name one; the server runs this instead of starting when its arguments hold it.</summary>
        public static readonly IReadOnlyList<string> Flags = ["--forget-plugin"];

        private const string UsageText = """
            Usage, with BYTESHELF_ROOT set to the server's root:
              ByteShelf.Server --forget-plugin <plugin id>
            """;

        /// <param name="args">The server's arguments.</param>
        /// <param name="rootPath"><c>BYTESHELF_ROOT</c>.</param>
        /// <param name="loggerFactory">Where what it did, or why not, is logged.</param>
        public static async Task<int> RunAsync(IReadOnlyList<string> args, string? rootPath, ILoggerFactory loggerFactory)
        {
            ILogger logger = loggerFactory.CreateLogger(typeof(PluginMetadataCommand));
            CommandArguments arguments = new CommandArguments(args);
            string? pluginId = arguments.Value("--forget-plugin");
            if (pluginId == null || !arguments.AllTaken)
            {
                logger.LogError("{Usage}", UsageText);
                return RootCommand.Usage;
            }

            return await RootCommand.RunAsync(rootPath, logger, startsARoot: false, async root =>
            {
                await using ServiceProvider services = RootCommand.BuildServices(ServerSettings.Open(root), loggerFactory);
                await using AsyncServiceScope scope = services.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ReplicationState>().RequireWritesAsync();
                return await ForgetAsync(pluginId, scope.ServiceProvider);
            });
        }

        private static async Task<string> ForgetAsync(string pluginId, IServiceProvider services)
        {
            await using IDbTransactionSession session = await services.GetRequiredService<IDbSessionFactory>().CreateSessionWithTransactionAsync();
            (int contents, int files) = await services.GetRequiredService<MetadataUpkeep>()
                .ForgetPluginAsync(pluginId, services.GetRequiredService<TimeProvider>().GetUtcNow(), session);
            await session.CommitAsync();

            return contents + files == 0
                ? $"The plugin '{pluginId}' kept nothing; nothing was deleted."
                : $"Deleted everything the plugin '{pluginId}' kept: about {contents} files' contents and {files} files.";
        }
    }
}
