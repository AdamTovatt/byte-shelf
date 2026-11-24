using ByteShelf.Server.Jobs.Models;
using ByteShelf.Server.Jobs.Services;
using ByteShelf.Server.Metadata.Models;
using ByteShelf.Server.Shared.Concurrency;

namespace ByteShelf.Server.Metadata.Services
{
    /// <summary>Runs <see cref="PluginMetadataReading"/> when <see cref="MetadataReadingWorker"/> runs the server's own.</summary>
    public class PluginMetadataReadingWorker : PeriodicJobWorker<PluginMetadataReadingSummary>
    {
        private readonly MetadataReadingSignal _signal;

        public PluginMetadataReadingWorker(IServiceScopeFactory scopeFactory, MetadataReadingSignal signal, TimeProvider timeProvider, ILogger<PluginMetadataReadingWorker> logger)
            : base(scopeFactory, timeProvider, logger)
        {
            _signal = signal;
        }

        protected override string JobName => JobNames.PluginMetadataReading;

        protected override TimeSpan Interval => MetadataReadingWorker.BetweenPasses;

        protected override WakeSignal Wake => _signal.PluginWake;

        protected override Task<PluginMetadataReadingSummary> RunOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            return services.GetRequiredService<PluginMetadataReading>().RunAsync(cancellationToken);
        }
    }
}
