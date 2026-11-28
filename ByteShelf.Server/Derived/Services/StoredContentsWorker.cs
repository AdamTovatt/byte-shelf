using ByteShelf.Server.Derived.Models;

namespace ByteShelf.Server.Derived.Services
{
    /// <summary>Makes what is made in the background of contents as they are stored, each plugin and group at its own pace.</summary>
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
            await Task.WhenAll(DerivedBackground.MakersOf(_plugins).Select(maker => RunMakerAsync(maker.Plugin, maker.Group, stoppingToken)));
        }

        private async Task RunMakerAsync(DerivedFilePlugin plugin, MakingGroup group, CancellationToken stoppingToken)
        {
            await foreach (string sha256 in _stored.ReadAllAsync(plugin.Id, group, stoppingToken))
            {
                try
                {
                    await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<DerivedBackground>().MakeStoredAsync(plugin, group, sha256, stoppingToken);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError(
                        exception,
                        "The plugin {Plugin} made nothing of the contents {Sha256} just stored; the job {JobName} tries again.",
                        plugin.Id,
                        sha256,
                        group.Start.Job);
                }
            }
        }
    }
}
