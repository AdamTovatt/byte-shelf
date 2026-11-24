using ByteShelf.Server.Derived.Services;
using ByteShelf.Server.Invalidation.Models;
using ByteShelf.Server.Storage.Repositories;

namespace ByteShelf.Server.Invalidation.Services
{
    /// <summary>
    /// Publishes that a shelf's contents are stale once a derived file its listings show has come or gone, which is in
    /// no change log: at once, and then at most every <see cref="Interval"/>, however many do.
    /// </summary>
    public sealed class DerivedInvalidationPublisher : BackgroundService
    {
        public static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

        private readonly DerivedChanges _changes;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IInvalidationBroadcaster _broadcaster;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<DerivedInvalidationPublisher> _logger;

        public DerivedInvalidationPublisher(
            DerivedChanges changes,
            IServiceScopeFactory scopeFactory,
            IInvalidationBroadcaster broadcaster,
            TimeProvider timeProvider,
            ILogger<DerivedInvalidationPublisher> logger)
        {
            _changes = changes;
            _scopeFactory = scopeFactory;
            _broadcaster = broadcaster;
            _timeProvider = timeProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (true)
            {
                IReadOnlyCollection<string> changed = await _changes.TakeAsync(_timeProvider, stoppingToken);
                try
                {
                    await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
                    foreach (Guid shelfId in await scope.ServiceProvider.GetRequiredService<IPlainContentsRepository>().GetShelvesAsync(changed))
                    {
                        _broadcaster.Publish(new InvalidationEvent(InvalidationResources.ShelfContents, shelfId.ToString()));
                    }
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    // Live updates are best-effort; a listing shows them when it is next fetched.
                    _logger.LogError(exception, "Publishing that the derived files of {Count} contents came or went failed.", changed.Count);
                }

                await Task.Delay(Interval, _timeProvider, stoppingToken);
            }
        }
    }
}
