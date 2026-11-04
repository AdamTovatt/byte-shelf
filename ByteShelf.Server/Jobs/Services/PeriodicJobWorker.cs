namespace ByteShelf.Server.Jobs.Services
{
    /// <summary>
    /// A background job that runs once at startup and then <see cref="Interval"/> after each pass ends,
    /// each pass in a scope of its own. A pass's summary is logged; a failed pass is logged and the next
    /// one runs on schedule.
    /// </summary>
    /// <typeparam name="TSummary">What one pass reports, logged when it ends.</typeparam>
    public abstract class PeriodicJobWorker<TSummary> : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger _logger;

        protected PeriodicJobWorker(IServiceScopeFactory scopeFactory, TimeProvider timeProvider, ILogger logger)
        {
            _scopeFactory = scopeFactory;
            _timeProvider = timeProvider;
            _logger = logger;
        }

        /// <summary>The job's name in <see cref="Models.JobNames"/>, which its log lines carry.</summary>
        protected abstract string JobName { get; }

        /// <summary>The time from the end of one pass to the start of the next.</summary>
        protected abstract TimeSpan Interval { get; }

        /// <summary>Does one pass with services from <paramref name="services"/>, a scope of its own.</summary>
        protected abstract Task<TSummary> RunOnceAsync(IServiceProvider services, CancellationToken cancellationToken);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using IServiceScope scope = _scopeFactory.CreateScope();
                    TSummary summary = await RunOnceAsync(scope.ServiceProvider, stoppingToken);
                    _logger.LogDebug("Job {JobName} finished a pass: {Summary}.", JobName, summary);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Job {JobName} failed; it runs again in {Interval}.", JobName, Interval);
                }

                try
                {
                    await Task.Delay(Interval, _timeProvider, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }
}
