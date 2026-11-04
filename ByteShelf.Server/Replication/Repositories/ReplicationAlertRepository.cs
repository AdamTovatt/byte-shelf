using System.Data.Common;
using ByteShelf.Server.Replication.Models;
using EasyReasy.Database;
using EasyReasy.Database.Mapping;

namespace ByteShelf.Server.Replication.Repositories
{
    public class ReplicationAlertRepository : RepositoryBase, IReplicationAlertRepository
    {
        public ReplicationAlertRepository(DbDataSource dataSource, IDbSessionFactory sessionFactory)
            : base(dataSource, sessionFactory)
        {
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<ReplicationAlert>> GetAllAsync(IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $@"
                    SELECT subject AS {nameof(AlertEntity.Subject)}, problem AS {nameof(AlertEntity.Problem)},
                        since AS {nameof(AlertEntity.Since)}, last_sent_at AS {nameof(AlertEntity.LastSentAt)}
                    FROM replication_alert
                    ORDER BY subject";

                IEnumerable<AlertEntity> entities = await dbSession.Connection.QueryAsync<AlertEntity>(query, transaction: dbSession.Transaction);
                return (IReadOnlyList<ReplicationAlert>)entities.Select(entity => entity.ToDomain()).ToList();
            }, session);
        }

        /// <inheritdoc />
        public async Task SaveAsync(ReplicationAlert alert, IDbSession? session = null)
        {
            await UseSessionAsync(async (dbSession) =>
            {
                string query = @"
                    INSERT INTO replication_alert (subject, problem, since, last_sent_at)
                    VALUES (@Subject, @Problem, @Since, @LastSentAt)
                    ON CONFLICT (subject) DO UPDATE SET problem = excluded.problem, since = excluded.since, last_sent_at = excluded.last_sent_at";

                return await dbSession.Connection.ExecuteAsync(
                    query, new { alert.Subject, alert.Problem, alert.Since, alert.LastSentAt }, transaction: dbSession.Transaction);
            }, session);
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string subject, IDbSession? session = null)
        {
            await UseSessionAsync(async (dbSession) =>
            {
                return await dbSession.Connection.ExecuteAsync(
                    $"DELETE FROM replication_alert WHERE subject = @{nameof(subject)}", new { subject }, transaction: dbSession.Transaction);
            }, session);
        }

        internal class AlertEntity
        {
            public string Subject { get; set; } = string.Empty;
            public string Problem { get; set; } = string.Empty;
            public DateTimeOffset Since { get; set; }
            public DateTimeOffset LastSentAt { get; set; }

            public ReplicationAlert ToDomain()
            {
                return new ReplicationAlert { Subject = Subject, Problem = Problem, Since = Since, LastSentAt = LastSentAt };
            }
        }
    }
}
