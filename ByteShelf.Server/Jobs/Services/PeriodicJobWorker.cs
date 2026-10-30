using ByteShelf.Server.Jobs.Models;

namespace ByteShelf.Server.Jobs.Services
{
    /// <summary>
    /// A background job that runs once at startup and then every <see cref="Interval"/>, each pass in a
    /// scope of its own and recorded in <c>job_run</c> through <see cref="IJobRunRecorder"/>. A failed
    /// pass is logged and the next one runs on schedule.
    /// </summary>
    /// <typeparam name="TSummary">What one pass reports, stored as the run's summary.</typeparam>
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

        /// <summary>The job's name in <see cref="JobNames"/>.</summary>
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
                    IJobRunRecorder recorder = scope.ServiceProvider.GetRequiredService<IJobRunRecorder>();

                    await recorder.RunAsync(
                        JobName,
                        async (CancellationToken token) =>
                        {
                            TSummary summary = await RunOnceAsync(scope.ServiceProvider, token);
                            return new JobRunOutcome<TSummary>(true, summary, null);
                        },
                        stoppingToken);
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
