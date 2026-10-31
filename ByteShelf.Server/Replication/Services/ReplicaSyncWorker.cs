namespace ByteShelf.Server.Replication.Services
{
    /// <summary>
    /// Keeps a replica following its primary: steps through <see cref="ReplicaSync"/> without waiting
    /// while there is more, then every <see cref="PollInterval"/>. A failed step is recorded for the
    /// admins and retried after a wait that doubles up to <see cref="MaxRetryDelay"/>. On a server that is
    /// not a replica a step only finds that out, so it costs one query per poll; a pairing wakes it at once.
    /// </summary>
    public sealed class ReplicaSyncWorker : BackgroundService
    {
        /// <summary>How soon a caught-up replica asks again.</summary>
        public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

        public static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(5);

        public static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(5);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ReplicaSyncControl _control;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<ReplicaSyncWorker> _logger;

        public ReplicaSyncWorker(IServiceScopeFactory scopeFactory, ReplicaSyncControl control, TimeProvider timeProvider, ILogger<ReplicaSyncWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _control = control;
            _timeProvider = timeProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            TimeSpan retryDelay = FirstRetryDelay;
            while (!stoppingToken.IsCancellationRequested)
            {
                TimeSpan wait;
                using (IServiceScope scope = _scopeFactory.CreateScope())
                {
                    ReplicaSync sync = scope.ServiceProvider.GetRequiredService<ReplicaSync>();
                    try
                    {
                        bool caughtUp = await sync.SyncOnceAsync(stoppingToken);
                        retryDelay = FirstRetryDelay;
                        wait = caughtUp ? PollInterval : TimeSpan.Zero;
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception exception)
                    {
                        _logger.LogWarning(exception, "Replication failed; trying again in {Delay}.", retryDelay);
                        await RecordAsync(sync, exception);
                        wait = retryDelay;
                        retryDelay = TimeSpan.FromTicks(Math.Min(retryDelay.Ticks * 2, MaxRetryDelay.Ticks));
                    }
                }

                if (wait > TimeSpan.Zero)
                {
                    try
                    {
                        await _control.WaitAsync(wait, _timeProvider, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        return;
                    }
                }
            }
        }

        private async Task RecordAsync(ReplicaSync sync, Exception exception)
        {
            try
            {
                await sync.RecordFailureAsync(exception);
            }
            catch (Exception recordFailure)
            {
                _logger.LogError(recordFailure, "Recording a replication failure failed too.");
            }
        }
    }
}
