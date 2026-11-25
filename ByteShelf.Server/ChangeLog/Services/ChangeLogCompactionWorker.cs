using ByteShelf.Server.ChangeLog.Models;
using ByteShelf.Server.ChangeLog.Repositories;
using ByteShelf.Server.Jobs.Models;
using ByteShelf.Server.Jobs.Services;
using EasyReasy.Database;

namespace ByteShelf.Server.ChangeLog.Services
{
    /// <summary>
    /// Compacts the change log hourly by <see cref="ChangeLogRetention"/>, on a primary and on a replica alike
    /// (<see cref="IChangeLogRepository.CompactAsync"/>). A replica behind anything dropped takes a whole copy.
    /// </summary>
    public sealed class ChangeLogCompactionWorker : PeriodicJobWorker<int>
    {
        private readonly TimeProvider _timeProvider;

        public ChangeLogCompactionWorker(IServiceScopeFactory scopeFactory, TimeProvider timeProvider, ILogger<ChangeLogCompactionWorker> logger)
            : base(scopeFactory, timeProvider, logger)
        {
            _timeProvider = timeProvider;
        }

        protected override string JobName => JobNames.ChangeLogCompaction;

        /// <summary>Hourly, so <see cref="ChangeLogRetention.PlainTraces"/> is kept to within about an hour while the server runs.</summary>
        protected override TimeSpan Interval => TimeSpan.FromHours(1);

        protected override async Task<int> RunOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            IDbSessionFactory sessionFactory = services.GetRequiredService<IDbSessionFactory>();
            await using IDbTransactionSession session = await sessionFactory.CreateSessionWithTransactionAsync();
            int dropped = await services.GetRequiredService<IChangeLogRepository>().CompactAsync(_timeProvider.GetUtcNow(), session);
            await session.CommitAsync();
            return dropped;
        }
    }
}
