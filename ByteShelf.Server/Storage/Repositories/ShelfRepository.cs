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
            string query = @"
                INSERT INTO shelf (id, name, quota_bytes, created_at, updated_at)
                VALUES (@Id, @Name, @QuotaBytes, @CreatedAt, @UpdatedAt)";

            try
            {
                await session.Connection.ExecuteAsync(
                    query,
                    new { shelf.Id, shelf.Name, shelf.QuotaBytes, shelf.CreatedAt, shelf.UpdatedAt },
                    transaction: session.Transaction);
            }
            catch (DbException exception) when (SqliteUniqueViolation.Is(exception))
            {
                return false;
            }

            await _changeLog.AppendAsync(ChangeEntityType.Shelf, shelf.Id, ChangeAction.Added, shelf, shelf.CreatedAt, session);
            return true;
        }

        /// <inheritdoc />
        public async Task<ShelfUsage> GetUsageAsync(Guid shelfId, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $@"
                    SELECT
                        (SELECT COALESCE(SUM(size_bytes), 0) FROM file WHERE shelf_id = @{nameof(shelfId)}) AS {nameof(ShelfUsageEntity.UsedBytes)},
                        (SELECT COALESCE(SUM(size_bytes), 0) FROM upload WHERE shelf_id = @{nameof(shelfId)}) AS {nameof(ShelfUsageEntity.ReservedBytes)},
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
