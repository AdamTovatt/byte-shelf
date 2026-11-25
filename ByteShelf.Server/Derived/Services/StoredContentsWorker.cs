using ByteShelf.Server.Jobs.Models;

namespace ByteShelf.Server.Derived.Services
{
    /// <summary>Makes what is made in the background of contents as they are stored, each plugin at its own pace.</summary>
    public class StoredContentsWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IDerivedFilePlugins _plugins;
        private readonly StoredContents _stored;
        private readonly ILogger<StoredContentsWorker> _logger;

        public StoredContentsWorker(IServiceScopeFactory scopeFactory, IDerivedFilePlugins plugins, StoredContents stored, ILogger<StoredContentsWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _plugins = plugins;
            _stored = stored;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.WhenAll(_plugins.All.Select(plugin => RunPluginAsync(plugin, stoppingToken)));
        }

        private async Task RunPluginAsync(DerivedFilePlugin plugin, CancellationToken stoppingToken)
        {
            await foreach (string sha256 in _stored.ReadAllAsync(plugin.Id, stoppingToken))
            {
                try
                {
                    await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<DerivedBackground>().MakeStoredAsync(plugin, sha256, stoppingToken);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError(
                        exception,
                        "The plugin {Plugin} made nothing of the contents {Sha256} just stored; the job {JobName} tries again.",
                        plugin.Id,
                        sha256,
                        JobNames.DerivedMaking);
                }
            }
        }
    }
}
