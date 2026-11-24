using ByteShelf.Server.Jobs.Models;
using ByteShelf.Server.Jobs.Services;
using ByteShelf.Server.Metadata.Models;
using ByteShelf.Server.Shared.Concurrency;

namespace ByteShelf.Server.Metadata.Services
{
    /// <summary>
    /// Runs <see cref="MetadataReading"/> at startup, whenever <see cref="MetadataReadingSignal"/> says
    /// contents were stored, and hourly besides, which picks up a promotion to primary and anything a
    /// failed pass left.
    /// </summary>
    public class MetadataReadingWorker : PeriodicJobWorker<MetadataReadingSummary>
    {
        internal static readonly TimeSpan BetweenPasses = TimeSpan.FromHours(1);

        private readonly MetadataReadingSignal _signal;

        public MetadataReadingWorker(IServiceScopeFactory scopeFactory, MetadataReadingSignal signal, TimeProvider timeProvider, ILogger<MetadataReadingWorker> logger)
            : base(scopeFactory, timeProvider, logger)
        {
            _signal = signal;
        }

        protected override string JobName => JobNames.MetadataReading;

        protected override TimeSpan Interval => BetweenPasses;

        protected override WakeSignal Wake => _signal.Wake;

        protected override Task<MetadataReadingSummary> RunOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            return services.GetRequiredService<MetadataReading>().RunAsync(cancellationToken);
        }
    }
}
