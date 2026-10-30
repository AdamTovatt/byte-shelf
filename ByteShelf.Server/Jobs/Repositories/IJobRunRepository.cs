using EasyReasy.Database;

namespace ByteShelf.Server.Jobs.Repositories
{
    /// <summary>
    /// Data access for the <c>job_run</c> table: the recorder inserts a "started" row and later
    /// completes it.
    /// </summary>
    public interface IJobRunRepository
    {
        /// <summary>
        /// Inserts a fresh <c>job_run</c> row with start time set and completion fields null.
        /// Returns the generated id so the caller can complete the row later.
        /// </summary>
        Task<Guid> InsertStartedAsync(string jobName, DateTimeOffset startedAtUtc, IDbSession? session = null);

        /// <summary>
        /// Marks a previously started run as complete: sets <c>completed_at</c>, <c>success</c>,
        /// the structured summary JSON (when supplied) and the error message (when supplied).
        /// </summary>
        /// <returns>True if the row existed and was updated; false otherwise.</returns>
        Task<bool> CompleteAsync(
            Guid id,
            DateTimeOffset completedAtUtc,
            bool success,
            string? resultSummaryJson,
            string? errorMessage,
            IDbSession? session = null);

        /// <summary>
        /// Completes every row with <c>completed_at IS NULL</c> by stamping <c>success = false</c>,
        /// the supplied <paramref name="errorMessage"/>, and <c>completed_at = started_at</c>. Used by
        /// <see cref="Services.JobRunStartupSweeper"/> at boot to close rows a killed process left
        /// open; on a single server with non-overlapping restarts every open row at boot is one.
        /// </summary>
        /// <returns>The number of rows that were swept.</returns>
        Task<int> CompleteOrphansAsync(string errorMessage, IDbSession? session = null);
    }
}
