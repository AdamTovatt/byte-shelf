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

        /// <summary>When this server started, from which <see cref="ReplicationAlerts"/> counts silence and failing.</summary>
        private readonly DateTimeOffset _startedAt;

        public ReplicationAlertWorker(IServiceScopeFactory scopeFactory, TimeProvider timeProvider, ILogger<ReplicationAlertWorker> logger)
            : base(scopeFactory, timeProvider, logger)
        {
            _startedAt = timeProvider.GetUtcNow();
        }

        protected override string JobName => JobNames.ReplicationAlerts;

        protected override TimeSpan Interval => CheckInterval;

        protected override Task<int> RunOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            return services.GetRequiredService<ReplicationAlerts>().CheckAsync(_startedAt, cancellationToken);
        }
    }
}
