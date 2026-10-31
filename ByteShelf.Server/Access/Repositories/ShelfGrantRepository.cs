using System.Data.Common;
using ByteShelf.Server.Access.Models;
using ByteShelf.Server.ChangeLog.Models;
using ByteShelf.Server.ChangeLog.Repositories;
using EasyReasy.Database;
using EasyReasy.Database.Mapping;

namespace ByteShelf.Server.Access.Repositories
{
    public class ShelfGrantRepository : RepositoryBase, IShelfGrantRepository
    {
        private const string SelectColumns = $@"
            g.id,
            g.shelf_id AS {nameof(ShelfGrantEntity.ShelfId)},
            g.user_id AS {nameof(ShelfGrantEntity.UserId)},
            g.permission,
            g.created_at AS {nameof(ShelfGrantEntity.CreatedAt)},
            g.updated_at AS {nameof(ShelfGrantEntity.UpdatedAt)}";

        private readonly IChangeLogRepository _changeLog;

        public ShelfGrantRepository(DbDataSource dataSource, IDbSessionFactory sessionFactory, IChangeLogRepository changeLog)
            : base(dataSource, sessionFactory)
        {
            _changeLog = changeLog;
        }

        /// <inheritdoc />
        public async Task<ShelfGrant?> GetAsync(Guid shelfId, Guid userId, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $"SELECT {SelectColumns} FROM shelf_grant g WHERE g.shelf_id = @{nameof(shelfId)} AND g.user_id = @{nameof(userId)}";

                ShelfGrantEntity? entity = await dbSession.Connection.QuerySingleOrDefaultAsync<ShelfGrantEntity>(
                    query, new { shelfId, userId }, transaction: dbSession.Transaction);

                return entity?.ToDomain();
            }, session);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<HeldShelfGrant>> GetForShelfAsync(Guid shelfId, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $@"
                    SELECT
                        {SelectColumns},
                        u.email,
                        u.display_name AS {nameof(HeldShelfGrantEntity.DisplayName)}
                    FROM shelf_grant g
                    JOIN user_account u ON u.id = g.user_id
                    WHERE g.shelf_id = @{nameof(shelfId)}
                    ORDER BY g.created_at, g.id";

                IEnumerable<HeldShelfGrantEntity> entities = await dbSession.Connection.QueryAsync<HeldShelfGrantEntity>(
                    query, new { shelfId }, transaction: dbSession.Transaction);

                return (IReadOnlyList<HeldShelfGrant>)entities.Select(entity => entity.ToHeld()).ToList();
            }, session);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<ShelfGrant>> GetForUserAsync(Guid userId, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $"SELECT {SelectColumns} FROM shelf_grant g WHERE g.user_id = @{nameof(userId)}";

                IEnumerable<ShelfGrantEntity> entities = await dbSession.Connection.QueryAsync<ShelfGrantEntity>(
                    query, new { userId }, transaction: dbSession.Transaction);

                return (IReadOnlyList<ShelfGrant>)entities.Select(entity => entity.ToDomain()).ToList();
            }, session);
        }

        /// <inheritdoc />
        public async Task InsertAsync(ShelfGrant grant, IDbSession session)
        {
            string permission = PermissionColumns.ToColumn(grant.Permission);

            string query = $@"
                INSERT INTO shelf_grant (id, shelf_id, user_id, permission, created_at, updated_at)
                VALUES (@Id, @ShelfId, @UserId, @{nameof(permission)}, @CreatedAt, @UpdatedAt)";

            await session.Connection.ExecuteAsync(
                query,
                new { grant.Id, grant.ShelfId, grant.UserId, permission, grant.CreatedAt, grant.UpdatedAt },
                transaction: session.Transaction);

            await _changeLog.AppendAsync(ChangeEntityType.ShelfGrant, grant.Id, ChangeAction.Added, grant, grant.CreatedAt, session);
        }

        /// <inheritdoc />
        public async Task UpdateAsync(ShelfGrant grant, IDbSession session)
        {
            string permission = PermissionColumns.ToColumn(grant.Permission);

            string query = $@"
                UPDATE shelf_grant
                SET permission = @{nameof(permission)}, updated_at = @UpdatedAt
                WHERE id = @Id";

            await session.Connection.ExecuteAsync(
                query, new { grant.Id, permission, grant.UpdatedAt }, transaction: session.Transaction);

            await _changeLog.AppendAsync(ChangeEntityType.ShelfGrant, grant.Id, ChangeAction.Updated, grant, grant.UpdatedAt, session);
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(Guid shelfId, Guid userId, DateTimeOffset deletedAt, IDbSession session)
        {
            string query = $"DELETE FROM shelf_grant WHERE shelf_id = @{nameof(shelfId)} AND user_id = @{nameof(userId)} RETURNING id";
            return await _changeLog.DeleteAndLogAsync(ChangeEntityType.ShelfGrant, query, new { shelfId, userId }, deletedAt, session) > 0;
        }

        /// <inheritdoc />
        public async Task DeleteForShelfAsync(Guid shelfId, DateTimeOffset deletedAt, IDbSession session)
        {
            string query = $"DELETE FROM shelf_grant WHERE shelf_id = @{nameof(shelfId)} RETURNING id";
            await _changeLog.DeleteAndLogAsync(ChangeEntityType.ShelfGrant, query, new { shelfId }, deletedAt, session);
        }

        /// <inheritdoc />
        public async Task DeleteForUserAsync(Guid userId, DateTimeOffset deletedAt, IDbSession session)
        {
            string query = $"DELETE FROM shelf_grant WHERE user_id = @{nameof(userId)} RETURNING id";
            await _changeLog.DeleteAndLogAsync(ChangeEntityType.ShelfGrant, query, new { userId }, deletedAt, session);
        }

        internal class ShelfGrantEntity
        {
            public Guid Id { get; set; }
            public Guid ShelfId { get; set; }
            public Guid UserId { get; set; }
            public string Permission { get; set; } = string.Empty;
            public DateTimeOffset CreatedAt { get; set; }
            public DateTimeOffset UpdatedAt { get; set; }

            public ShelfGrant ToDomain()
            {
                return new ShelfGrant
                {
                    Id = Id,
                    ShelfId = ShelfId,
                    UserId = UserId,
                    Permission = PermissionColumns.ToAccessPermission(Permission),
                    CreatedAt = CreatedAt,
                    UpdatedAt = UpdatedAt,
                };
            }
        }

        internal class HeldShelfGrantEntity : ShelfGrantEntity
        {
            public string Email { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;

            public HeldShelfGrant ToHeld()
            {
                return new HeldShelfGrant { Grant = ToDomain(), Email = Email, DisplayName = DisplayName };
            }
        }
    }
}
