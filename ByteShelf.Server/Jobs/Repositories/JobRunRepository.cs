using System.Data.Common;
using EasyReasy.Database;
using EasyReasy.Database.Mapping;

namespace ByteShelf.Server.Jobs.Repositories
{
    /// <summary>
    /// SQLite implementation of <see cref="IJobRunRepository"/> for the <c>job_run</c> table.
    /// </summary>
    public class JobRunRepository : RepositoryBase, IJobRunRepository
    {
        public JobRunRepository(DbDataSource dataSource, IDbSessionFactory sessionFactory)
            : base(dataSource, sessionFactory)
        {
        }

        /// <inheritdoc />
        public async Task<Guid> InsertStartedAsync(string jobName, DateTimeOffset startedAtUtc, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                Guid id = Guid.NewGuid();

                string query = $@"
                    INSERT INTO job_run (id, job_name, started_at)
                    VALUES (@{nameof(id)}, @{nameof(jobName)}, @{nameof(startedAtUtc)})";

                await dbSession.Connection.ExecuteAsync(
                    query, new { id, jobName, startedAtUtc }, transaction: dbSession.Transaction);

                return id;
            }, session);
        }

        /// <inheritdoc />
        public async Task<bool> CompleteAsync(
            Guid id,
            DateTimeOffset completedAtUtc,
            bool success,
            string? resultSummaryJson,
            string? errorMessage,
            IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $@"
                    UPDATE job_run
                    SET completed_at = @{nameof(completedAtUtc)},
                        success = @{nameof(success)},
                        result_summary = @{nameof(resultSummaryJson)},
                        error_message = @{nameof(errorMessage)}
                    WHERE id = @{nameof(id)}";

                int rowsAffected = await dbSession.Connection.ExecuteAsync(
                    query,
                    new { id, completedAtUtc, success, resultSummaryJson, errorMessage },
                    transaction: dbSession.Transaction);

                return rowsAffected > 0;
            }, session);
        }

        /// <inheritdoc />
        public async Task<int> CompleteOrphansAsync(string errorMessage, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                // completed_at = started_at keeps the apparent duration honest: the run is only known
                // not to have finished before the next boot.
                string query = $@"
                    UPDATE job_run
                    SET completed_at = started_at,
                        success = 0,
                        error_message = @{nameof(errorMessage)}
                    WHERE completed_at IS NULL";

                return await dbSession.Connection.ExecuteAsync(
                    query, new { errorMessage }, transaction: dbSession.Transaction);
            }, session);
        }
    }
}
