using ByteShelf.Server.Jobs.Models;
using ByteShelf.Server.Jobs.Services;
using ByteShelf.Server.Storage.Models;

namespace ByteShelf.Server.Storage.Services
{
    /// <summary>Runs <see cref="IStorageMaintenance"/> at startup and then every <see cref="StorageOptions.MaintenanceInterval"/>.</summary>
    public class StorageMaintenanceWorker : PeriodicJobWorker<StorageMaintenanceSummary>
    {
        private readonly StorageOptions _options;
        private readonly DateTimeOffset _processStartedAt;

        public StorageMaintenanceWorker(
            IServiceScopeFactory scopeFactory,
            StorageOptions options,
            TimeProvider timeProvider,
            ILogger<StorageMaintenanceWorker> logger)
            : base(scopeFactory, timeProvider, logger)
        {
            _options = options;

            // Made once per process, before any request is served: every single-PUT upload started
            // before this belongs to a request that did not survive the restart.
            _processStartedAt = timeProvider.GetUtcNow();
        }

        protected override string JobName => JobNames.StorageMaintenance;

        protected override TimeSpan Interval => _options.MaintenanceInterval;

        protected override Task<StorageMaintenanceSummary> RunOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            return services.GetRequiredService<IStorageMaintenance>().RunAsync(_processStartedAt, cancellationToken);
        }
    }
}
