using ByteShelf.Server.Jobs.Models;
using ByteShelf.Server.Jobs.Repositories;

namespace ByteShelf.Server.Jobs.Services
{
    /// <summary>
    /// One-shot hosted service that runs on host boot and completes any <c>job_run</c> rows left
    /// in-flight by a previous process — the recorder's graceful-shutdown path handles
    /// <see cref="OperationCanceledException"/> with its own sentinel, but <c>kill -9</c>, OOM,
    /// or host crashes leave rows with <c>completed_at = NULL</c> forever, which then outrank
    /// every subsequent completed row in the latest-per-job query. Stamps the orphans with the
    /// <see cref="JobRunSentinels.UncleanShutdownErrorMessage"/> sentinel so consuming surfaces
    /// can render them as a neutral "cancelled" state rather than chasing a phantom outage.
    /// Register this before any recording consumer.
    /// </summary>
    public class JobRunStartupSweeper : IHostedService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<JobRunStartupSweeper> _logger;

        public JobRunStartupSweeper(
            IServiceScopeFactory scopeFactory,
            ILogger<JobRunStartupSweeper> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        /// <inheritdoc />
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            // Boot CT — if the host is already shutting down before the sweep even starts, bail
            // cleanly rather than open a DB session we'd just have to abandon.
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                IJobRunRepository repository = scope.ServiceProvider.GetRequiredService<IJobRunRepository>();

                // Single backend instance with non-overlapping restarts: every in-flight row at
                // boot is a genuine orphan from a previous process, so sweep them all.
                int swept = await repository.CompleteOrphansAsync(
                    JobRunSentinels.UncleanShutdownErrorMessage);

                if (swept > 0)
                {
                    _logger.LogWarning(
                        "Swept {Count} orphan job_run rows from a previous unclean shutdown.",
                        swept);
                }
            }
            catch (Exception exception)
            {
                // The sweep is a best-effort cleanup, not a hard startup gate — failing to write
                // doesn't justify aborting host boot. The orphans will get swept on the next
                // successful startup; meanwhile status surfaces may show one or two stale
                // "in flight" rows until then. Log so the failure isn't silent.
                _logger.LogError(exception, "Job_run orphan sweep failed; orphans (if any) will be retried on next startup.");
            }
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
