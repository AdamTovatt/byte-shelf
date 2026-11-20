using System.Data.Common;
using ByteShelf.Server.Access.Models;
using ByteShelf.Server.ChangeLog.Models;
using ByteShelf.Server.ChangeLog.Repositories;
using ByteShelf.Server.Shared.Database;
using EasyReasy.Database;
using EasyReasy.Database.Mapping;

namespace ByteShelf.Server.Access.Repositories
{
    public class ShelfKeyGrantRepository : RepositoryBase, IShelfKeyGrantRepository
    {
        private const string SelectColumns = $@"
            g.id,
            g.key_id AS {nameof(ShelfKeyGrantEntity.KeyId)},
            g.shelf_id AS {nameof(ShelfKeyGrantEntity.ShelfId)},
            g.permission,
            g.created_at AS {nameof(ShelfKeyGrantEntity.CreatedAt)},
            g.updated_at AS {nameof(ShelfKeyGrantEntity.UpdatedAt)}";

        private const string InsertSql = @"
            INSERT INTO shelf_key_grant (id, key_id, shelf_id, permission, created_at, updated_at)
            VALUES (@Id, @KeyId, @ShelfId, @Permission, @CreatedAt, @UpdatedAt)";

        private static readonly string UpsertReplicatedSql = UpsertSql.OnIdConflict(InsertSql);

        private readonly IChangeLogRepository _changeLog;

        public ShelfKeyGrantRepository(DbDataSource dataSource, IDbSessionFactory sessionFactory, IChangeLogRepository changeLog)
            : base(dataSource, sessionFactory)
        {
            _changeLog = changeLog;
        }

        /// <inheritdoc />
        public async Task<ShelfKeyGrant?> GetAsync(Guid shelfId, Guid keyId, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $"SELECT {SelectColumns} FROM shelf_key_grant g WHERE g.key_id = @{nameof(keyId)} AND g.shelf_id = @{nameof(shelfId)}";

                ShelfKeyGrantEntity? entity = await dbSession.Connection.QuerySingleOrDefaultAsync<ShelfKeyGrantEntity>(
                    query, new { keyId, shelfId }, transaction: dbSession.Transaction);

                return entity?.ToDomain();
            }, session);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<HeldShelfKeyGrant>> GetForKeyAsync(Guid keyId, IDbSession? session = null)
        {
            return await GetHeldAsync($"g.key_id = @{nameof(keyId)}", new { keyId }, session);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<HeldShelfKeyGrant>> GetForKeysOnShelfAsync(Guid shelfId, IDbSession? session = null)
        {
            return await GetHeldAsync($"g.key_id IN (SELECT key_id FROM shelf_key_grant WHERE shelf_id = @{nameof(shelfId)})", new { shelfId }, session);
        }

        /// <summary>The grants <paramref name="where"/> picks with their shelves' names, in one statement, oldest first.</summary>
        private async Task<IReadOnlyList<HeldShelfKeyGrant>> GetHeldAsync(string where, object parameters, IDbSession? session)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $@"
                    SELECT {SelectColumns}, s.name AS {nameof(HeldShelfKeyGrantEntity.ShelfName)}
                    FROM shelf_key_grant g
                    JOIN shelf s ON s.id = g.shelf_id
                    WHERE {where}
                    ORDER BY g.created_at, g.id";

                IEnumerable<HeldShelfKeyGrantEntity> entities = await dbSession.Connection.QueryAsync<HeldShelfKeyGrantEntity>(
                    query, parameters, transaction: dbSession.Transaction);

                return (IReadOnlyList<HeldShelfKeyGrant>)entities.Select(entity => entity.ToHeld()).ToList();
            }, session);
        }

        /// <inheritdoc />
        public async Task InsertAsync(ShelfKeyGrant grant, IDbSession session)
        {
            await session.Connection.ExecuteAsync(InsertSql, Parameters(grant), transaction: session.Transaction);
            await _changeLog.AppendAsync(ChangeEntityType.ShelfKeyGrant, grant.Id, ChangeAction.Added, grant, grant.CreatedAt, session);
        }

        /// <inheritdoc />
        public async Task UpdateAsync(ShelfKeyGrant grant, IDbSession session)
        {
            string permission = PermissionColumns.ToColumn(grant.Permission);

            string query = $@"
                UPDATE shelf_key_grant
                SET permission = @{nameof(permission)}, updated_at = @UpdatedAt
                WHERE id = @Id";

            await session.Connection.ExecuteAsync(
                query, new { grant.Id, permission, grant.UpdatedAt }, transaction: session.Transaction);

            await _changeLog.AppendAsync(ChangeEntityType.ShelfKeyGrant, grant.Id, ChangeAction.Updated, grant, grant.UpdatedAt, session);
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(Guid shelfId, Guid keyId, DateTimeOffset deletedAt, IDbSession session)
        {
            string query = $"DELETE FROM shelf_key_grant WHERE key_id = @{nameof(keyId)} AND shelf_id = @{nameof(shelfId)} RETURNING id";
            return await _changeLog.DeleteAndLogAsync(ChangeEntityType.ShelfKeyGrant, query, new { keyId, shelfId }, deletedAt, session) > 0;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<Guid>> DeleteForShelfAsync(Guid shelfId, DateTimeOffset deletedAt, IDbSession session)
        {
            // Each grant names its key, but DeleteAndLogAsync answers only how many it removed; read the
            // keys first, inside the same write transaction.
            string keysQuery = $"SELECT key_id FROM shelf_key_grant WHERE shelf_id = @{nameof(shelfId)}";
            List<Guid> keyIds = (await session.Connection.QueryAsync<Guid>(keysQuery, new { shelfId }, transaction: session.Transaction)).ToList();

            string query = $"DELETE FROM shelf_key_grant WHERE shelf_id = @{nameof(shelfId)} RETURNING id";
            await _changeLog.DeleteAndLogAsync(ChangeEntityType.ShelfKeyGrant, query, new { shelfId }, deletedAt, session);
            return keyIds;
        }

        /// <inheritdoc />
        public async Task DeleteForKeyAsync(Guid keyId, DateTimeOffset deletedAt, IDbSession session)
        {
            string query = $"DELETE FROM shelf_key_grant WHERE key_id = @{nameof(keyId)} RETURNING id";
            await _changeLog.DeleteAndLogAsync(ChangeEntityType.ShelfKeyGrant, query, new { keyId }, deletedAt, session);
        }

        /// <inheritdoc />
        public async Task UpsertReplicatedAsync(ShelfKeyGrant grant, string action, DateTimeOffset at, IDbSession session)
        {
            string asideQuery = "DELETE FROM shelf_key_grant WHERE key_id = @KeyId AND shelf_id = @ShelfId AND id <> @Id RETURNING id";
            await _changeLog.DeleteAndLogAsync(ChangeEntityType.ShelfKeyGrant, asideQuery, new { grant.KeyId, grant.ShelfId, grant.Id }, at, session);

            await session.Connection.ExecuteAsync(UpsertReplicatedSql, Parameters(grant), transaction: session.Transaction);
            await _changeLog.AppendAsync(ChangeEntityType.ShelfKeyGrant, grant.Id, action, grant, at, session);
        }

        /// <inheritdoc />
        public async Task<bool> DeleteByIdAsync(Guid id, DateTimeOffset deletedAt, IDbSession session)
        {
            string query = $"DELETE FROM shelf_key_grant WHERE id = @{nameof(id)} RETURNING id";
            return await _changeLog.DeleteAndLogAsync(ChangeEntityType.ShelfKeyGrant, query, new { id }, deletedAt, session) > 0;
        }

        private static object Parameters(ShelfKeyGrant grant)
        {
            return new
            {
                grant.Id,
                grant.KeyId,
                grant.ShelfId,
                Permission = PermissionColumns.ToColumn(grant.Permission),
                grant.CreatedAt,
                grant.UpdatedAt,
            };
        }

        internal class ShelfKeyGrantEntity
        {
            public Guid Id { get; set; }
            public Guid KeyId { get; set; }
            public Guid ShelfId { get; set; }
            public string Permission { get; set; } = string.Empty;
            public DateTimeOffset CreatedAt { get; set; }
            public DateTimeOffset UpdatedAt { get; set; }

            public ShelfKeyGrant ToDomain()
            {
                return new ShelfKeyGrant
                {
                    Id = Id,
                    KeyId = KeyId,
                    ShelfId = ShelfId,
                    Permission = PermissionColumns.ToKeyPermission(Permission),
                    CreatedAt = CreatedAt,
                    UpdatedAt = UpdatedAt,
                };
            }
        }

        internal class HeldShelfKeyGrantEntity : ShelfKeyGrantEntity
        {
            public string ShelfName { get; set; } = string.Empty;

            public HeldShelfKeyGrant ToHeld()
            {
                return new HeldShelfKeyGrant { Grant = ToDomain(), ShelfName = ShelfName };
            }
        }
    }
}
