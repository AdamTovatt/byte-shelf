using ByteShelf.Server.Jobs.Models;
using ByteShelf.Server.Jobs.Services;

namespace ByteShelf.Server.Replication.Services
{
    /// <summary>
    /// Runs <see cref="ReplicationAlerts"/> every <see cref="CheckInterval"/>, a pass's summary being how
    /// many alerts it sent. On a server neither a primary with replicas nor a replica a pass is three
    /// queries and sends nothing.
    /// </summary>
    public sealed class ReplicationAlertWorker : PeriodicJobWorker<int>
    {
        /// <summary>How often replication is looked at; small beside the alert threshold, which is in minutes.</summary>
        public static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);

        public ReplicationAlertWorker(IServiceScopeFactory scopeFactory, TimeProvider timeProvider, ILogger<ReplicationAlertWorker> logger)
            : base(scopeFactory, timeProvider, logger)
        {
        }

        protected override string JobName => JobNames.ReplicationAlerts;

        protected override TimeSpan Interval => CheckInterval;

        protected override Task<int> RunOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            return services.GetRequiredService<ReplicationAlerts>().CheckAsync(cancellationToken);
        }
    }
}
