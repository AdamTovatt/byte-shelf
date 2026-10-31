using System.Data.Common;
using ByteShelf.Server.Access.Models;
using ByteShelf.Server.ChangeLog.Models;
using ByteShelf.Server.ChangeLog.Repositories;
using EasyReasy.Database;
using EasyReasy.Database.Mapping;

namespace ByteShelf.Server.Access.Repositories
{
    public class ShelfKeyRepository : RepositoryBase, IShelfKeyRepository
    {
        private const string SelectColumns = $@"
            id,
            shelf_id AS {nameof(ShelfKeyEntity.ShelfId)},
            name,
            permission,
            secret_sha256 AS {nameof(ShelfKeyEntity.SecretSha256)},
            created_at AS {nameof(ShelfKeyEntity.CreatedAt)}";

        private readonly IChangeLogRepository _changeLog;

        public ShelfKeyRepository(DbDataSource dataSource, IDbSessionFactory sessionFactory, IChangeLogRepository changeLog)
            : base(dataSource, sessionFactory)
        {
            _changeLog = changeLog;
        }

        /// <inheritdoc />
        public async Task<ShelfKey?> GetBySecretSha256Async(string secretSha256, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $"SELECT {SelectColumns} FROM shelf_key WHERE secret_sha256 = @{nameof(secretSha256)}";

                ShelfKeyEntity? entity = await dbSession.Connection.QuerySingleOrDefaultAsync<ShelfKeyEntity>(
                    query, new { secretSha256 }, transaction: dbSession.Transaction);

                return entity?.ToDomain();
            }, session);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<ShelfKey>> GetForShelfAsync(Guid shelfId, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $"SELECT {SelectColumns} FROM shelf_key WHERE shelf_id = @{nameof(shelfId)} ORDER BY created_at, id";

                IEnumerable<ShelfKeyEntity> entities = await dbSession.Connection.QueryAsync<ShelfKeyEntity>(
                    query, new { shelfId }, transaction: dbSession.Transaction);

                return (IReadOnlyList<ShelfKey>)entities.Select(entity => entity.ToDomain()).ToList();
            }, session);
        }

        /// <inheritdoc />
        public async Task InsertAsync(ShelfKey key, IDbSession session)
        {
            string permission = PermissionColumns.ToColumn(key.Permission);

            string query = $@"
                INSERT INTO shelf_key (id, shelf_id, name, permission, secret_sha256, created_at)
                VALUES (@Id, @ShelfId, @Name, @{nameof(permission)}, @SecretSha256, @CreatedAt)";

            await session.Connection.ExecuteAsync(
                query,
                new { key.Id, key.ShelfId, key.Name, permission, key.SecretSha256, key.CreatedAt },
                transaction: session.Transaction);

            await _changeLog.AppendAsync(ChangeEntityType.ShelfKey, key.Id, ChangeAction.Added, key, key.CreatedAt, session);
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(Guid shelfId, Guid keyId, DateTimeOffset deletedAt, IDbSession session)
        {
            string query = $"DELETE FROM shelf_key WHERE id = @{nameof(keyId)} AND shelf_id = @{nameof(shelfId)} RETURNING id";
            return await _changeLog.DeleteAndLogAsync(ChangeEntityType.ShelfKey, query, new { shelfId, keyId }, deletedAt, session) > 0;
        }

        /// <inheritdoc />
        public async Task DeleteForShelfAsync(Guid shelfId, DateTimeOffset deletedAt, IDbSession session)
        {
            string query = $"DELETE FROM shelf_key WHERE shelf_id = @{nameof(shelfId)} RETURNING id";
            await _changeLog.DeleteAndLogAsync(ChangeEntityType.ShelfKey, query, new { shelfId }, deletedAt, session);
        }

        internal class ShelfKeyEntity
        {
            public Guid Id { get; set; }
            public Guid ShelfId { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Permission { get; set; } = string.Empty;
            public string SecretSha256 { get; set; } = string.Empty;
            public DateTimeOffset CreatedAt { get; set; }

            public ShelfKey ToDomain()
            {
                return new ShelfKey
                {
                    Id = Id,
                    ShelfId = ShelfId,
                    Name = Name,
                    Permission = PermissionColumns.ToKeyPermission(Permission),
                    SecretSha256 = SecretSha256,
                    CreatedAt = CreatedAt,
                };
            }
        }
    }
}
