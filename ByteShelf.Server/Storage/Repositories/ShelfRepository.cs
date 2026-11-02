using System.Data.Common;
using ByteShelf.Server.ChangeLog.Models;
using ByteShelf.Server.ChangeLog.Repositories;
using ByteShelf.Server.Shared.Database;
using ByteShelf.Server.Storage.Models;
using EasyReasy.Database;
using EasyReasy.Database.Mapping;

namespace ByteShelf.Server.Storage.Repositories
{
    public class ShelfRepository : RepositoryBase, IShelfRepository
    {
        private const string SelectColumns = $@"
            id,
            name,
            quota_bytes AS {nameof(ShelfEntity.QuotaBytes)},
            created_at AS {nameof(ShelfEntity.CreatedAt)},
            updated_at AS {nameof(ShelfEntity.UpdatedAt)}";

        private const string InsertSql = @"
            INSERT INTO shelf (id, name, quota_bytes, created_at, updated_at)
            VALUES (@Id, @Name, @QuotaBytes, @CreatedAt, @UpdatedAt)";

        private static readonly string UpsertReplicatedSql = UpsertSql.OnIdConflict(InsertSql);

        /// <summary>
        /// How much of its shelf's room an upload holds: its whole size, or for a swap only what it adds to
        /// its file's size now, since finishing replaces the one with the other. Two swaps of one file
        /// both count from its size, but only one of them can finish.
        /// </summary>
        private const string ReservedBytesSql = @"
            CASE WHEN upload.swap_file_id IS NULL THEN upload.size_bytes
            ELSE MAX(0, upload.size_bytes - COALESCE((SELECT swapped.size_bytes FROM file AS swapped WHERE swapped.id = upload.swap_file_id), 0))
            END";

        /// <summary>What a shelf's name becomes, before its id, while a replicated shelf takes the name over; '~' is in no shelf name.</summary>
        private const string SetAsideNamePrefix = "~";

        private readonly IChangeLogRepository _changeLog;

        public ShelfRepository(DbDataSource dataSource, IDbSessionFactory sessionFactory, IChangeLogRepository changeLog)
            : base(dataSource, sessionFactory)
        {
            _changeLog = changeLog;
        }

        /// <inheritdoc />
        public async Task<Shelf?> GetByIdAsync(Guid id, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $"SELECT {SelectColumns} FROM shelf WHERE id = @{nameof(id)}";

                ShelfEntity? entity = await dbSession.Connection.QuerySingleOrDefaultAsync<ShelfEntity>(
                    query, new { id }, transaction: dbSession.Transaction);

                return entity?.ToDomain();
            }, session);
        }

        /// <inheritdoc />
        public async Task<Shelf?> GetByNameAsync(string name, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $"SELECT {SelectColumns} FROM shelf WHERE name = @{nameof(name)}";

                ShelfEntity? entity = await dbSession.Connection.QuerySingleOrDefaultAsync<ShelfEntity>(
                    query, new { name }, transaction: dbSession.Transaction);

                return entity?.ToDomain();
            }, session);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<Shelf>> GetAllAsync(IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $"SELECT {SelectColumns} FROM shelf ORDER BY name";

                IEnumerable<ShelfEntity> entities = await dbSession.Connection.QueryAsync<ShelfEntity>(
                    query, transaction: dbSession.Transaction);

                return (IReadOnlyList<Shelf>)entities.Select(entity => entity.ToDomain()).ToList();
            }, session);
        }

        /// <inheritdoc />
        public async Task<bool> TryInsertAsync(Shelf shelf, IDbSession session)
        {
            try
            {
                await session.Connection.ExecuteAsync(InsertSql, Parameters(shelf), transaction: session.Transaction);
            }
            catch (DbException exception) when (SqliteUniqueViolation.Is(exception))
            {
                return false;
            }

            await _changeLog.AppendAsync(ChangeEntityType.Shelf, shelf.Id, ChangeAction.Added, shelf, shelf.CreatedAt, session);
            return true;
        }

        /// <inheritdoc />
        public async Task<bool> TryUpdateAsync(Shelf shelf, IDbSession session)
        {
            string query = @"
                UPDATE shelf
                SET name = @Name, quota_bytes = @QuotaBytes, updated_at = @UpdatedAt
                WHERE id = @Id";

            try
            {
                await session.Connection.ExecuteAsync(
                    query,
                    new { shelf.Id, shelf.Name, shelf.QuotaBytes, shelf.UpdatedAt },
                    transaction: session.Transaction);
            }
            catch (DbException exception) when (SqliteUniqueViolation.Is(exception))
            {
                return false;
            }

            await _changeLog.AppendAsync(ChangeEntityType.Shelf, shelf.Id, ChangeAction.Updated, shelf, shelf.UpdatedAt, session);
            return true;
        }

        /// <inheritdoc />
        public async Task<bool> IsEmptyAsync(Guid shelfId, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $@"
                    SELECT NOT EXISTS (SELECT 1 FROM file WHERE shelf_id = @{nameof(shelfId)})
                       AND NOT EXISTS (SELECT 1 FROM directory WHERE shelf_id = @{nameof(shelfId)} AND parent_id IS NOT NULL)
                       AND NOT EXISTS (SELECT 1 FROM upload WHERE shelf_id = @{nameof(shelfId)})";

                long empty = await dbSession.Connection.QuerySingleAsync<long>(
                    query, new { shelfId }, transaction: dbSession.Transaction);

                return empty != 0;
            }, session);
        }

        /// <inheritdoc />
        public async Task DeleteAsync(Guid shelfId, DateTimeOffset deletedAt, IDbSession session)
        {
            await session.Connection.ExecuteAsync(
                $"DELETE FROM shelf WHERE id = @{nameof(shelfId)}", new { shelfId }, transaction: session.Transaction);

            await _changeLog.AppendAsync<Shelf>(ChangeEntityType.Shelf, shelfId, ChangeAction.Purged, null, deletedAt, session);
        }

        /// <inheritdoc />
        public async Task UpsertReplicatedAsync(Shelf shelf, string action, DateTimeOffset at, IDbSession session)
        {
            // Set aside under a name no shelf can be given, until the log names it again.
            string asideQuery = $@"
                UPDATE shelf
                SET name = '{SetAsideNamePrefix}' || id, updated_at = @{nameof(at)}
                WHERE name = @Name AND id <> @Id
                RETURNING {SelectColumns}";

            IEnumerable<ShelfEntity> setAside = await session.Connection.QueryAsync<ShelfEntity>(
                asideQuery, new { shelf.Name, shelf.Id, at }, transaction: session.Transaction);

            foreach (ShelfEntity aside in setAside)
            {
                await _changeLog.AppendAsync(ChangeEntityType.Shelf, aside.Id, ChangeAction.Updated, aside.ToDomain(), at, session);
            }

            await session.Connection.ExecuteAsync(UpsertReplicatedSql, Parameters(shelf), transaction: session.Transaction);
            await _changeLog.AppendAsync(ChangeEntityType.Shelf, shelf.Id, action, shelf, at, session);
        }

        /// <inheritdoc />
        public async Task<ShelfUsage> GetUsageAsync(Guid shelfId, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $@"
                    SELECT
                        (SELECT COALESCE(SUM(size_bytes), 0) FROM file WHERE shelf_id = @{nameof(shelfId)}) AS {nameof(ShelfUsageEntity.UsedBytes)},
                        (SELECT COALESCE(SUM({ReservedBytesSql}), 0) FROM upload WHERE shelf_id = @{nameof(shelfId)}) AS {nameof(ShelfUsageEntity.ReservedBytes)},
                        (SELECT COUNT(*) FROM file WHERE shelf_id = @{nameof(shelfId)} AND deleted_at IS NULL) AS {nameof(ShelfUsageEntity.FileCount)}";

                ShelfUsageEntity entity = await dbSession.Connection.QuerySingleAsync<ShelfUsageEntity>(
                    query, new { shelfId }, transaction: dbSession.Transaction);

                return new ShelfUsage
                {
                    UsedBytes = entity.UsedBytes,
                    ReservedBytes = entity.ReservedBytes,
                    FileCount = (int)entity.FileCount,
                };
            }, session);
        }

        private static object Parameters(Shelf shelf)
        {
            return new { shelf.Id, shelf.Name, shelf.QuotaBytes, shelf.CreatedAt, shelf.UpdatedAt };
        }

        internal class ShelfEntity
        {
            public Guid Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public long? QuotaBytes { get; set; }
            public DateTimeOffset CreatedAt { get; set; }
            public DateTimeOffset UpdatedAt { get; set; }

            public Shelf ToDomain()
            {
                return new Shelf
                {
                    Id = Id,
                    Name = Name,
                    QuotaBytes = QuotaBytes,
                    CreatedAt = CreatedAt,
                    UpdatedAt = UpdatedAt,
                };
            }
        }

        internal class ShelfUsageEntity
        {
            public long UsedBytes { get; set; }
            public long ReservedBytes { get; set; }
            public long FileCount { get; set; }
        }
    }
}
