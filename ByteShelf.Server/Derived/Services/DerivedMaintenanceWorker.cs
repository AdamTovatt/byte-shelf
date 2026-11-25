using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Jobs.Models;
using ByteShelf.Server.Jobs.Services;

namespace ByteShelf.Server.Derived.Services
{
    /// <summary>Runs <see cref="DerivedMaintenance"/> at startup and then daily.</summary>
    public class DerivedMaintenanceWorker : PeriodicJobWorker<DerivedMaintenanceSummary>
    {
        public DerivedMaintenanceWorker(IServiceScopeFactory scopeFactory, TimeProvider timeProvider, ILogger<DerivedMaintenanceWorker> logger)
            : base(scopeFactory, timeProvider, logger)
        {
        }

        protected override string JobName => JobNames.DerivedMaintenance;

        protected override TimeSpan Interval => TimeSpan.FromDays(1);

        protected override Task<DerivedMaintenanceSummary> RunOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            return services.GetRequiredService<DerivedMaintenance>().RunAsync(cancellationToken);
        }
    }
}
