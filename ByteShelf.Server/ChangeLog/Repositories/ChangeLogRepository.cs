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

            if (ChangeEntityType.ForgetsEarlierStates(entityType))
            {
                // Each earlier entry then reads as a removal, which a follower replaying it applies harmlessly
                // before this one: there is always one after it, written with the blanking.
                string forgetQuery = $@"
                    UPDATE change_log SET payload = NULL
                    WHERE entity_type = @{nameof(entityType)} AND entity_id = @{nameof(entityId)} AND payload IS NOT NULL";

                await session.Connection.ExecuteAsync(forgetQuery, new { entityType, entityId }, transaction: session.Transaction);
            }

            await session.Connection.ExecuteAsync(
                query, new { entityType, entityId, action, payload, createdAt }, transaction: session.Transaction);
        }

        /// <inheritdoc />
        public Task<int> DeleteAndLogAsync(string entityType, string deleteReturningIds, object parameters, DateTimeOffset at, IDbSession session)
        {
            return DeleteAndLogAsAsync(ChangeAction.Purged, entityType, deleteReturningIds, parameters, at, session);
        }

        /// <inheritdoc />
        public Task<int> ReleaseAndLogAsync(string entityType, string deleteReturningIds, object parameters, DateTimeOffset at, IDbSession session)
        {
            return DeleteAndLogAsAsync(ChangeAction.Released, entityType, deleteReturningIds, parameters, at, session);
        }

        private async Task<int> DeleteAndLogAsAsync(string removal, string entityType, string deleteReturningIds, object parameters, DateTimeOffset at, IDbSession session)
        {
            List<Guid> deleted = (await session.Connection.QueryAsync<Guid>(
                deleteReturningIds, parameters, transaction: session.Transaction)).ToList();

            foreach (Guid id in deleted)
            {
                await AppendAsync<object>(entityType, id, removal, null, at, session);
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
        public Task<ChangeLogPage> GetPageAsync(long afterSequence, ChangeLogPageLimit limit, IDbSession session)
        {
            return ChangeLogPages.ReadAsync("change_log", "1", afterSequence, limit, session);
        }

        /// <inheritdoc />
        public Task<ChangeLogPage> GetLatestStatesPageAsync(long afterSequence, ChangeLogPageLimit limit, IDbSession session)
        {
            string newest = @"
                NOT EXISTS (
                    SELECT 1 FROM change_log AS newer
                    WHERE newer.entity_type = entry.entity_type
                      AND newer.entity_id = entry.entity_id
                      AND newer.sequence > entry.sequence)";

            return ChangeLogPages.ReadAsync("change_log", newest, afterSequence, limit, session);
        }

        /// <inheritdoc />
        public async Task<int> CompactAsync(DateTimeOffset now, IDbSession session)
        {
            DateTimeOffset before = now - ChangeLogRetention.Entries;
            DateTimeOffset plainTracesBefore = now - ChangeLogRetention.PlainTraces;

            List<long> dropped = new List<long>();
            dropped.AddRange(await DeleteReturningSequencesAsync(SupersededOrRemovedSql(), new { before }, session));
            dropped.AddRange(await DeleteReturningSequencesAsync(
                PlainStatesSql, new { plainTracesBefore, ChangeEntityType.EncryptedFlag.EntityType, ChangeEntityType.EncryptedFlag.JsonPath }, session));
            dropped.AddRange(await DeleteReturningSequencesAsync(ReleasedSql(), new { plainTracesBefore, released = ChangeAction.Released }, session));

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

        /// <summary>
        /// Entries older than <c>@before</c> that a newer one for the same entity supersedes, removals that old,
        /// and those of rows that went with their owner unlogged, whose newest entry would otherwise stay for good.
        /// </summary>
        private static string SupersededOrRemovedSql()
        {
            string goneWithOwner = string.Concat(ChangeEntityType.Owned.Select(owned => $@"
                       OR (entity_type = '{owned.Key}'
                           AND NOT EXISTS (SELECT 1 FROM {owned.Value.OwnedTable} WHERE id = change_log.entity_id))"));

            return $@"
                DELETE FROM change_log
                WHERE created_at < @before
                  AND (payload IS NULL
                       OR EXISTS (
                           SELECT 1 FROM change_log AS newer
                           WHERE newer.entity_type = change_log.entity_type
                             AND newer.entity_id = change_log.entity_id
                             AND newer.sequence > change_log.sequence){goneWithOwner})
                RETURNING sequence";
        }

        /// <summary>
        /// A file's entries whose state is not encrypted, before an entry of the file whose state is that was made
        /// before <c>@plainTracesBefore</c>. Found from the encrypted entries, through the entity index. By the state
        /// and not the swapped action: a replica that took a whole copy logs the file's newer state without one.
        /// </summary>
        private const string PlainStatesSql = @"
            DELETE FROM change_log
            WHERE sequence IN (
                SELECT plain.sequence
                FROM change_log AS encrypted
                JOIN change_log AS plain
                  ON plain.entity_type = encrypted.entity_type
                 AND plain.entity_id = encrypted.entity_id
                 AND plain.sequence < encrypted.sequence
                WHERE encrypted.entity_type = @EntityType
                  AND encrypted.created_at < @plainTracesBefore
                  AND json_extract(encrypted.payload, @JsonPath) = 1
                  AND json_extract(plain.payload, @JsonPath) = 0)
            RETURNING sequence";

        /// <summary>
        /// The entries of what was read from contents up to and with its release, made before
        /// <c>@plainTracesBefore</c>: the states forgotten before it name the contents by their id as well.
        /// </summary>
        private static string ReleasedSql()
        {
            string readFromContents = string.Join(", ", ChangeEntityType.ReadFromContents.Select(type => $"'{type}'"));

            return $@"
                DELETE FROM change_log
                WHERE sequence IN (
                    SELECT traced.sequence
                    FROM change_log AS released
                    JOIN change_log AS traced
                      ON traced.entity_type = released.entity_type
                     AND traced.entity_id = released.entity_id
                     AND traced.sequence <= released.sequence
                    WHERE released.entity_type IN ({readFromContents})
                      AND released.action = @released
                      AND released.created_at < @plainTracesBefore)
                RETURNING sequence";
        }

        private static async Task<IEnumerable<long>> DeleteReturningSequencesAsync(string deleteQuery, object parameters, IDbSession session)
        {
            return await session.Connection.QueryAsync<long>(deleteQuery, parameters, transaction: session.Transaction);
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
