using System.Data.Common;
using ByteShelf.Server.ChangeLog.Models;
using EasyReasy.Database;
using EasyReasy.Database.Mapping;

namespace ByteShelf.Server.Replication.Repositories
{
    public class ReplicationCopyRepository : RepositoryBase, IReplicationCopyRepository
    {
        private const string SelectColumns = $@"
            sequence,
            entity_type AS {nameof(CopyEntity.EntityType)},
            entity_id AS {nameof(CopyEntity.EntityId)},
            action,
            payload,
            created_at AS {nameof(CopyEntity.CreatedAt)}";

        public ReplicationCopyRepository(DbDataSource dataSource, IDbSessionFactory sessionFactory)
            : base(dataSource, sessionFactory)
        {
        }

        /// <inheritdoc />
        public async Task ClearAsync(IDbSession? session = null)
        {
            await UseSessionAsync(async (dbSession) =>
            {
                return await dbSession.Connection.ExecuteAsync("DELETE FROM replication_copy", transaction: dbSession.Transaction);
            }, session);
        }

        /// <inheritdoc />
        public async Task AddAsync(IReadOnlyList<ChangeLogEntry> entries, IDbSession session)
        {
            // An entity copied again on a later page (it changed meanwhile) keeps only its newer state.
            string query = @"
                DELETE FROM replication_copy WHERE entity_type = @EntityType AND entity_id = @EntityId;
                INSERT INTO replication_copy (sequence, entity_type, entity_id, action, payload, created_at)
                VALUES (@Sequence, @EntityType, @EntityId, @Action, @Payload, @CreatedAt)";

            foreach (ChangeLogEntry entry in entries)
            {
                await session.Connection.ExecuteAsync(
                    query,
                    new { entry.Sequence, entry.EntityType, entry.EntityId, entry.Action, entry.Payload, entry.CreatedAt },
                    transaction: session.Transaction);
            }
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<ChangeLogEntry>> GetAfterAsync(long afterSequence, int limit, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $@"
                    SELECT {SelectColumns}
                    FROM replication_copy
                    WHERE sequence > @{nameof(afterSequence)}
                    ORDER BY sequence
                    LIMIT @{nameof(limit)}";

                IEnumerable<CopyEntity> entities = await dbSession.Connection.QueryAsync<CopyEntity>(
                    query, new { afterSequence, limit }, transaction: dbSession.Transaction);

                return (IReadOnlyList<ChangeLogEntry>)entities.Select(entity => entity.ToDomain()).ToList();
            }, session);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<Guid>> GetIdsMissingAsync(string entityType, string table, IDbSession session)
        {
            string query = $@"
                SELECT id FROM {table}
                WHERE id NOT IN (SELECT entity_id FROM replication_copy WHERE entity_type = @{nameof(entityType)})";

            return (await session.Connection.QueryAsync<Guid>(query, new { entityType }, transaction: session.Transaction)).ToList();
        }

        internal class CopyEntity
        {
            public long Sequence { get; set; }
            public string EntityType { get; set; } = string.Empty;
            public Guid EntityId { get; set; }
            public string Action { get; set; } = string.Empty;
            public string Payload { get; set; } = string.Empty;
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
