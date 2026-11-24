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
        public Task<ChangeLogPage> GetPageAsync(long afterSequence, ChangeLogPageLimit limit, IDbSession session)
        {
            return ChangeLogPages.ReadAsync("replication_copy", "1", afterSequence, limit, session);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<Guid>> GetIdsMissingAsync(string entityType, string table, EntityOwner? owner, IDbSession session)
        {
            string ownerCopied = owner == null ? string.Empty : $@"
                  AND {owner.OwnedColumn} IN (SELECT entity_id FROM replication_copy WHERE entity_type = @ownerType)";

            string query = $@"
                SELECT id FROM {table}
                WHERE id NOT IN (SELECT entity_id FROM replication_copy WHERE entity_type = @{nameof(entityType)}){ownerCopied}";

            return (await session.Connection.QueryAsync<Guid>(query, new { entityType, ownerType = owner?.EntityType }, transaction: session.Transaction)).ToList();
        }
    }
}
