using ByteShelf.Server.ChangeLog.Repositories;
using ByteShelf.Server.Jobs.Models;
using ByteShelf.Server.Jobs.Services;
using EasyReasy.Database;

namespace ByteShelf.Server.ChangeLog.Services
{
    /// <summary>
    /// Compacts the change log daily: an entry older than <see cref="Retention"/> goes once a newer one
    /// for its entity exists, and a removal goes once it is that old. A replica behind anything dropped,
    /// which in practice means one that has not followed the log for that long, takes a whole copy.
    /// </summary>
    public sealed class ChangeLogCompactionWorker : PeriodicJobWorker<int>
    {
        /// <summary>How long every entry is kept, and so how long a replica may fall behind before it needs a copy.</summary>
        public static readonly TimeSpan Retention = TimeSpan.FromDays(30);

        private readonly TimeProvider _timeProvider;

        public ChangeLogCompactionWorker(IServiceScopeFactory scopeFactory, TimeProvider timeProvider, ILogger<ChangeLogCompactionWorker> logger)
            : base(scopeFactory, timeProvider, logger)
        {
            _timeProvider = timeProvider;
        }

        protected override string JobName => JobNames.ChangeLogCompaction;

        protected override TimeSpan Interval => TimeSpan.FromDays(1);

        protected override async Task<int> RunOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            IDbSessionFactory sessionFactory = services.GetRequiredService<IDbSessionFactory>();
            await using IDbTransactionSession session = await sessionFactory.CreateSessionWithTransactionAsync();
            int dropped = await services.GetRequiredService<IChangeLogRepository>().CompactAsync(_timeProvider.GetUtcNow() - Retention, session);
            await session.CommitAsync();
            return dropped;
        }
    }
}
