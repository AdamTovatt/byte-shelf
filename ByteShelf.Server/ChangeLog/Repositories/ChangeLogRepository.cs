using System.Data.Common;
using ByteShelf.Server.ChangeLog.Models;
using EasyReasy.Database;
using EasyReasy.Database.Mapping;

namespace ByteShelf.Server.ChangeLog.Repositories
{
    public class ChangeLogRepository : RepositoryBase, IChangeLogRepository
    {
        private const string SelectColumns = $@"
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
                return await dbSession.Connection.QuerySingleAsync<long>(
                    "SELECT COALESCE(MAX(sequence), 0) FROM change_log", transaction: dbSession.Transaction);
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
