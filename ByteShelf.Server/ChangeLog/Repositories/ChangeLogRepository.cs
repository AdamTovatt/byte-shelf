using System.Data.Common;
using ByteShelf.Server.ChangeLog.Models;
using EasyReasy.Database;
using EasyReasy.Database.Mapping;

namespace ByteShelf.Server.ChangeLog.Repositories
{
    public class ChangeLogRepository : RepositoryBase, IChangeLogRepository
    {
        /// <summary>
        /// The columns of an entry, as <see cref="ChangeLogEntity"/> reads them; any table holding entries
        /// in the change log's shape (a replica's <c>replication_copy</c>) reads them the same way.
        /// </summary>
        internal const string SelectColumns = $@"
            sequence,
            entity_type AS {nameof(ChangeLogEntity.EntityType)},
            entity_id AS {nameof(ChangeLogEntity.EntityId)},
            action,
            payload,
            created_at AS {nameof(ChangeLogEntity.CreatedAt)}";

        public ChangeLogRepository(DbDataSource dataSource, IDbSessionFactory sessionFactory)
            : base(dataSource, sessionFactory)
        {
        }

        /// <inheritdoc />
        public async Task AppendAsync<TState>(string entityType, Guid entityId, string action, TState? state, DateTimeOffset at, IDbSession session)
            where TState : class
        {
            string? payload = state == null ? null : ChangePayload.Serialize(state);
            DateTimeOffset createdAt = at;

            string query = $@"
                INSERT INTO change_log (entity_type, entity_id, action, payload, created_at)
                VALUES (@{nameof(entityType)}, @{nameof(entityId)}, @{nameof(action)}, @{nameof(payload)}, @{nameof(createdAt)})";

            await session.Connection.ExecuteAsync(
                query, new { entityType, entityId, action, payload, createdAt }, transaction: session.Transaction);
        }

        /// <inheritdoc />
        public async Task<int> DeleteAndLogAsync(string entityType, string deleteReturningIds, object parameters, DateTimeOffset at, IDbSession session)
        {
            List<Guid> deleted = (await session.Connection.QueryAsync<Guid>(
                deleteReturningIds, parameters, transaction: session.Transaction)).ToList();

            foreach (Guid id in deleted)
            {
                await AppendAsync<object>(entityType, id, ChangeAction.Purged, null, at, session);
            }

            return deleted.Count;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<ChangeLogEntry>> GetAfterAsync(long afterSequence, int limit, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $@"
                    SELECT {SelectColumns}
                    FROM change_log
                    WHERE sequence > @{nameof(afterSequence)}
                    ORDER BY sequence
                    LIMIT @{nameof(limit)}";

                IEnumerable<ChangeLogEntity> entities = await dbSession.Connection.QueryAsync<ChangeLogEntity>(
                    query, new { afterSequence, limit }, transaction: dbSession.Transaction);

                return (IReadOnlyList<ChangeLogEntry>)entities.Select(entity => entity.ToDomain()).ToList();
            }, session);
        }

        /// <inheritdoc />
        public async Task<long> GetLatestSequenceAsync(IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                // AUTOINCREMENT's counter, not MAX(sequence): compaction may drop the newest entries.
                return await dbSession.Connection.QuerySingleAsync<long>(
                    "SELECT COALESCE((SELECT seq FROM sqlite_sequence WHERE name = 'change_log'), 0)", transaction: dbSession.Transaction);
            }, session);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<ChangeLogEntry>> GetLatestStatesAfterAsync(long afterSequence, int limit, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $@"
                    SELECT {SelectColumns}
                    FROM change_log AS entry
                    WHERE entry.sequence > @{nameof(afterSequence)}
                      AND NOT EXISTS (
                          SELECT 1 FROM change_log AS newer
                          WHERE newer.entity_type = entry.entity_type
                            AND newer.entity_id = entry.entity_id
                            AND newer.sequence > entry.sequence)
                    ORDER BY entry.sequence
                    LIMIT @{nameof(limit)}";

                IEnumerable<ChangeLogEntity> entities = await dbSession.Connection.QueryAsync<ChangeLogEntity>(
                    query, new { afterSequence, limit }, transaction: dbSession.Transaction);

                return (IReadOnlyList<ChangeLogEntry>)entities.Select(entity => entity.ToDomain()).ToList();
            }, session);
        }

        /// <inheritdoc />
        public async Task<int> CompactAsync(DateTimeOffset before, IDbSession session)
        {
            string deleteQuery = $@"
                DELETE FROM change_log
                WHERE created_at < @{nameof(before)}
                  AND (payload IS NULL OR EXISTS (
                      SELECT 1 FROM change_log AS newer
                      WHERE newer.entity_type = change_log.entity_type
                        AND newer.entity_id = change_log.entity_id
                        AND newer.sequence > change_log.sequence))
                RETURNING sequence";

            List<long> dropped = (await session.Connection.QueryAsync<long>(deleteQuery, new { before }, transaction: session.Transaction)).ToList();
            if (dropped.Count > 0)
            {
                long prunedThrough = dropped.Max();
                await session.Connection.ExecuteAsync(
                    $"UPDATE change_log_state SET pruned_through = MAX(pruned_through, @{nameof(prunedThrough)})",
                    new { prunedThrough },
                    transaction: session.Transaction);
            }

            return dropped.Count;
        }

        /// <inheritdoc />
        public async Task<long> GetPrunedThroughAsync(IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                return await dbSession.Connection.QuerySingleAsync<long>(
                    "SELECT pruned_through FROM change_log_state", transaction: dbSession.Transaction);
            }, session);
        }

        /// <inheritdoc />
        public async Task<ChangeLogEntry?> GetBySequenceAsync(long sequence, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                ChangeLogEntity? entity = await dbSession.Connection.QuerySingleOrDefaultAsync<ChangeLogEntity>(
                    $"SELECT {SelectColumns} FROM change_log WHERE sequence = @{nameof(sequence)}", new { sequence }, transaction: dbSession.Transaction);

                return entity?.ToDomain();
            }, session);
        }

        internal class ChangeLogEntity
        {
            public long Sequence { get; set; }
            public string EntityType { get; set; } = string.Empty;
            public Guid EntityId { get; set; }
            public string Action { get; set; } = string.Empty;
            public string? Payload { get; set; }
            public DateTimeOffset CreatedAt { get; set; }

            public ChangeLogEntry ToDomain()
            {
                return new ChangeLogEntry
                {
                    Sequence = Sequence,
                    EntityType = EntityType,
                    EntityId = EntityId,
                    Action = Action,
                    Payload = Payload,
                    CreatedAt = CreatedAt,
                };
            }
        }
    }
}
