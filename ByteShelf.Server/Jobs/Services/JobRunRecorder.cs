using System.Text.Json;
using ByteShelf.Server.Jobs.Models;
using ByteShelf.Server.Jobs.Repositories;
using ByteShelf.Server.Shared.Json;

namespace ByteShelf.Server.Jobs.Services
{
    /// <summary>
    /// Default implementation of <see cref="IJobRunRecorder"/>. Inserts a "started" row, awaits the
    /// callback, then completes the row with the outcome — recording failures with the exception
    /// message and rethrowing so callers' existing top-level logging still fires.
    /// </summary>
    public class JobRunRecorder : IJobRunRecorder
    {
        /// <summary>The longest <c>error_message</c> the recorder persists; longer ones are clipped with an ellipsis.</summary>
        private const int MaxErrorMessageLength = 400;

        /// <summary>Stamped when a failed run reaches the recorder without any message of its own.</summary>
        private const string MissingFailureMessage = "Job failed without an error message.";

        private readonly IJobRunRepository _repository;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<JobRunRecorder> _logger;

        public JobRunRecorder(IJobRunRepository repository, TimeProvider timeProvider, ILogger<JobRunRecorder> logger)
        {
            _repository = repository;
            _timeProvider = timeProvider;
            _logger = logger;
        }

        /// <inheritdoc />
        public async Task RunAsync<TSummary>(
            string jobName,
            Func<CancellationToken, Task<JobRunOutcome<TSummary>>> work,
            CancellationToken shutdownToken)
        {
            DateTimeOffset startedAt = _timeProvider.GetUtcNow();
            Guid runId = await _repository.InsertStartedAsync(jobName, startedAt);

            JobRunOutcome<TSummary> outcome;
            try
            {
                outcome = await work(shutdownToken);
            }
            catch (OperationCanceledException) when (shutdownToken.IsCancellationRequested)
            {
                // Graceful shutdown: stamp the row so status surfaces don't keep surfacing it as
                // "in flight" forever (orphans would otherwise outrank later completed runs in the
                // latest-per-job query for up to a full schedule cycle).
                await TryCompleteAsync(
                    runId,
                    startedAt,
                    success: false,
                    resultSummaryJson: null,
                    errorMessage: JobRunSentinels.ShutdownErrorMessage,
                    jobName);
                throw;
            }
            catch (Exception exception)
            {
                // Fall back to the exception type when Message is blank: a custom exception with no
                // message would otherwise reach the job_run failure CHECK with an empty
                // error_message. TryCompleteAsync coalesces blanks too, but the type name is a more
                // useful audit value than the generic sentinel.
                string errorMessage = string.IsNullOrWhiteSpace(exception.Message)
                    ? exception.GetType().FullName ?? "Unknown exception"
                    : exception.Message;

                await TryCompleteAsync(
                    runId,
                    startedAt,
                    success: false,
                    resultSummaryJson: null,
                    errorMessage: errorMessage,
                    jobName);
                throw;
            }

            // Serialise after the work-exception try: a summary that can't be serialised is a
            // recorder problem, not a work failure. Persist the row as the outcome reports it,
            // just without the summary, and log so the problem isn't lost.
            string? summaryJson = TrySerializeSummary(outcome.Summary, jobName);

            await TryCompleteAsync(
                runId,
                startedAt,
                outcome.Success,
                summaryJson,
                outcome.ErrorMessage,
                jobName);
        }

        private string? TrySerializeSummary<TSummary>(TSummary? summary, string jobName)
        {
            if (summary is null)
            {
                return null;
            }

            try
            {
                return JsonSerializer.Serialize(summary, JsonDefaults.CamelCase);
            }
            catch (Exception serializeException)
            {
                _logger.LogError(
                    serializeException,
                    "Failed to serialise job_run summary for job {JobName}; persisting completion without it.",
                    jobName);
                return null;
            }
        }

        /// <summary>
        /// Guarantees a non-empty, length-bounded message for a failed completion: coalesces a
        /// null/blank message to <see cref="MissingFailureMessage"/> and clips anything over
        /// <see cref="MaxErrorMessageLength"/> UTF-16 code units (with an ellipsis, without
        /// splitting a surrogate pair).
        /// </summary>
        private static string NormalizeFailureMessage(string? errorMessage)
        {
            string message = string.IsNullOrWhiteSpace(errorMessage)
                ? MissingFailureMessage
                : errorMessage.Trim();

            if (message.Length <= MaxErrorMessageLength)
            {
                return message;
            }

            // Reserve one code unit for the ellipsis; step back one more if the cut would split a
            // surrogate pair.
            int clipLength = MaxErrorMessageLength - 1;
            if (char.IsHighSurrogate(message[clipLength - 1]))
            {
                clipLength--;
            }

            return message[..clipLength] + "…";
        }

        /// <summary>
        /// Best-effort completion write: a failure here must not mask the work's original exception
        /// (which the catch block above is already rethrowing — callers' outer handlers log it),
        /// so we just log the completion-write failure itself.
        /// </summary>
        private async Task TryCompleteAsync(
            Guid runId,
            DateTimeOffset startedAt,
            bool success,
            string? resultSummaryJson,
            string? errorMessage,
            string jobName)
        {
            // Clamp the completion instant up to the start instant. If wall-clock time retreats
            // between InsertStartedAsync and here (e.g. an NTP correction lands mid-run), an
            // unclamped "now" earlier than started_at would violate the
            // job_run_completion_after_start CHECK on the table — the UPDATE would throw, get
            // swallowed below, and a run that actually finished would be left looking interrupted.
            // Clamping records it as completed with a zero-length duration instead.
            DateTimeOffset now = _timeProvider.GetUtcNow();
            DateTimeOffset completedAt = now < startedAt ? startedAt : now;

            // A success=false row must carry a non-empty error_message (the
            // job_run_status_consistent CHECK rejects a null/blank one). A successful run always
            // writes a null error_message: the CHECK forbids a non-null one, so we drop any
            // message a caller mistakenly attached to a successful outcome rather than letting it
            // throw the UPDATE and orphan the row.
            string? errorMessageToWrite = success ? null : NormalizeFailureMessage(errorMessage);

            try
            {
                bool completed = await _repository.CompleteAsync(
                    runId,
                    completedAt,
                    success,
                    resultSummaryJson,
                    errorMessageToWrite);

                if (!completed)
                {
                    // CompleteAsync returns false when the UPDATE matched no row — the started row
                    // vanished between insert and completion (it shouldn't, given the single writer).
                    // Surface it rather than completing silently into the void.
                    _logger.LogWarning(
                        "job_run completion for job {JobName} run {RunId} matched no row; the started row was not found.",
                        jobName,
                        runId);
                }
            }
            catch (Exception completionException)
            {
                _logger.LogError(
                    completionException,
                    "Failed to write job_run completion for job {JobName} run {RunId}.",
                    jobName,
                    runId);
            }
        }
    }
}
