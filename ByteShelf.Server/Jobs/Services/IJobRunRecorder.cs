using ByteShelf.Server.Jobs.Models;

namespace ByteShelf.Server.Jobs.Services
{
    /// <summary>
    /// Wraps a unit of background-job work in a try/finally that records start, completion (with
    /// summary) or failure (with error message) to the <c>job_run</c> table. Callers can't forget
    /// the finally — the recorder owns it. Every reachable run records a completion row so status
    /// surfaces never show orphaned in-flight rows; the cause is encoded in the row's
    /// <c>success</c>/<c>error_message</c>.
    /// </summary>
    public interface IJobRunRecorder
    {
        /// <summary>
        /// Records a start row, runs <paramref name="work"/>, then completes the row based on the
        /// returned <see cref="JobRunOutcome{TSummary}"/>. On exception the row is completed with
        /// success=false and the exception message, then the exception rethrows so callers'
        /// existing logging fires.
        /// </summary>
        /// <param name="jobName">The canonical job name (see <see cref="JobNames"/>).</param>
        /// <param name="work">The work to execute.</param>
        /// <param name="shutdownToken">
        /// The hosted service's stopping token. When the work throws <see cref="OperationCanceledException"/>
        /// and this token is cancelled the recorder completes the row with success=false and a
        /// well-known sentinel error message (<see cref="JobRunSentinels.ShutdownErrorMessage"/>) so
        /// consumers can present it as a neutral "cancelled" state rather than a real failure.
        /// </param>
        /// <typeparam name="TSummary">The summary's static type.</typeparam>
        Task RunAsync<TSummary>(
            string jobName,
            Func<CancellationToken, Task<JobRunOutcome<TSummary>>> work,
            CancellationToken shutdownToken);
    }
}
