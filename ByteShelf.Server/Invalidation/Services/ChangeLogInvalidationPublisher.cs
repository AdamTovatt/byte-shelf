using ByteShelf.Server.ChangeLog.Models;
using ByteShelf.Server.ChangeLog.Repositories;
using ByteShelf.Server.Invalidation.Models;

namespace ByteShelf.Server.Invalidation.Services
{
    /// <summary>
    /// Follows the change log and publishes what each new entry makes stale. The log holds only
    /// committed changes, and SQLite commits one writer at a time in sequence order, so an event is
    /// never published for a change that then rolls back, and none is missed.
    /// </summary>
    public sealed class ChangeLogInvalidationPublisher : BackgroundService
    {
        /// <summary>How long a change may wait before open web UIs hear of it.</summary>
        public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

        internal const int BatchSize = 500;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IInvalidationBroadcaster _broadcaster;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<ChangeLogInvalidationPublisher> _logger;

        public ChangeLogInvalidationPublisher(
            IServiceScopeFactory scopeFactory,
            IInvalidationBroadcaster broadcaster,
            TimeProvider timeProvider,
            ILogger<ChangeLogInvalidationPublisher> logger)
        {
            _scopeFactory = scopeFactory;
            _broadcaster = broadcaster;
            _timeProvider = timeProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            long? published = null;
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // The first pass reads where the log stands: changes made before startup are no news
                    // to a web UI that connects after it. Inside the try, so a failed read is tried again
                    // like a failed poll rather than stopping the host.
                    published = published == null
                        ? await ReadAsync(repository => repository.GetLatestSequenceAsync())
                        : await PublishAfterAsync(published.Value);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    // Live updates are best-effort; the next poll tries again from the same place.
                    _logger.LogError(exception, "Publishing invalidations from the change log failed.");
                }

                await Task.Delay(PollInterval, _timeProvider, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }

        /// <summary>Publishes everything after <paramref name="published"/> and answers the last sequence it covered.</summary>
        internal async Task<long> PublishAfterAsync(long published)
        {
            while (true)
            {
                long after = published;
                IReadOnlyList<ChangeLogEntry> entries = await ReadAsync(repository => repository.GetAfterAsync(after, BatchSize));
                if (entries.Count == 0)
                {
                    return published;
                }

                foreach (InvalidationEvent invalidation in entries.SelectMany(ChangeLogInvalidations.For).Distinct())
                {
                    _broadcaster.Publish(invalidation);
                }

                published = entries[^1].Sequence;
            }
        }

        private async Task<T> ReadAsync<T>(Func<IChangeLogRepository, Task<T>> read)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            return await read(scope.ServiceProvider.GetRequiredService<IChangeLogRepository>());
        }
    }
}
