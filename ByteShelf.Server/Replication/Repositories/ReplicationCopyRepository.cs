using System.Data.Common;
using ByteShelf.Server.ChangeLog.Models;
using ByteShelf.Server.ChangeLog.Repositories;
using EasyReasy.Database;
using EasyReasy.Database.Mapping;

namespace ByteShelf.Server.Replication.Repositories
{
    public class ReplicationCopyRepository : RepositoryBase, IReplicationCopyRepository
    {
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
            // An entity copied again on a later page (it changed meanwhile) keeps only its newer state, and
            // one removed meanwhile is dropped.
            string deleteQuery = "DELETE FROM replication_copy WHERE entity_type = @EntityType AND entity_id = @EntityId";
            string insertQuery = @"
                INSERT INTO replication_copy (sequence, entity_type, entity_id, action, payload, created_at)
                VALUES (@Sequence, @EntityType, @EntityId, @Action, @Payload, @CreatedAt)";

            foreach (ChangeLogEntry entry in entries)
            {
                await session.Connection.ExecuteAsync(
                    deleteQuery, new { entry.EntityType, entry.EntityId }, transaction: session.Transaction);

                if (entry.Payload != null)
                {
                    await session.Connection.ExecuteAsync(
                        insertQuery,
                        new { entry.Sequence, entry.EntityType, entry.EntityId, entry.Action, entry.Payload, entry.CreatedAt },
                        transaction: session.Transaction);
                }
            }
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<ChangeLogEntry>> GetAfterAsync(long afterSequence, int limit, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $@"
                    SELECT {ChangeLogRepository.SelectColumns}
                    FROM replication_copy
                    WHERE sequence > @{nameof(afterSequence)}
                    ORDER BY sequence
                    LIMIT @{nameof(limit)}";

                IEnumerable<ChangeLogRepository.ChangeLogEntity> entities = await dbSession.Connection.QueryAsync<ChangeLogRepository.ChangeLogEntity>(
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
    }
}
