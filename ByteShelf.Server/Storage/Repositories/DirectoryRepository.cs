using System.Data.Common;
using ByteShelf.Server.ChangeLog.Models;
using ByteShelf.Server.ChangeLog.Repositories;
using ByteShelf.Server.Storage.Models;
using EasyReasy.Database;
using EasyReasy.Database.Mapping;

namespace ByteShelf.Server.Storage.Repositories
{
    public class DirectoryRepository : RepositoryBase, IDirectoryRepository
    {
        private const string SelectColumns = $@"
            id,
            shelf_id AS {nameof(DirectoryEntity.ShelfId)},
            parent_id AS {nameof(DirectoryEntity.ParentId)},
            name,
            created_at AS {nameof(DirectoryEntity.CreatedAt)},
            updated_at AS {nameof(DirectoryEntity.UpdatedAt)},
            deleted_at AS {nameof(DirectoryEntity.DeletedAt)},
            deletion_id AS {nameof(DirectoryEntity.DeletionId)}";

        private readonly IChangeLogRepository _changeLog;

        public DirectoryRepository(DbDataSource dataSource, IDbSessionFactory sessionFactory, IChangeLogRepository changeLog)
            : base(dataSource, sessionFactory)
        {
            _changeLog = changeLog;
        }

        /// <inheritdoc />
        public async Task<DirectoryRecord?> GetByIdAsync(Guid id, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $"SELECT {SelectColumns} FROM directory WHERE id = @{nameof(id)}";

                DirectoryEntity? entity = await dbSession.Connection.QuerySingleOrDefaultAsync<DirectoryEntity>(
                    query, new { id }, transaction: dbSession.Transaction);

                return entity?.ToDomain();
            }, session);
        }

        /// <inheritdoc />
        public async Task<DirectoryRecord> GetRootAsync(Guid shelfId, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $"SELECT {SelectColumns} FROM directory WHERE shelf_id = @{nameof(shelfId)} AND parent_id IS NULL";

                DirectoryEntity entity = await dbSession.Connection.QuerySingleAsync<DirectoryEntity>(
                    query, new { shelfId }, transaction: dbSession.Transaction);

                return entity.ToDomain();
            }, session);
        }

        /// <inheritdoc />
        public async Task<DirectoryRecord?> GetLiveChildAsync(Guid parentId, string name, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $@"
                    SELECT {SelectColumns}
                    FROM directory
                    WHERE parent_id = @{nameof(parentId)} AND name = @{nameof(name)} AND deleted_at IS NULL";

                DirectoryEntity? entity = await dbSession.Connection.QuerySingleOrDefaultAsync<DirectoryEntity>(
                    query, new { parentId, name }, transaction: dbSession.Transaction);

                return entity?.ToDomain();
            }, session);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<DirectoryRecord>> GetLiveChildrenAsync(Guid parentId, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $@"
                    SELECT {SelectColumns}
                    FROM directory
                    WHERE parent_id = @{nameof(parentId)} AND deleted_at IS NULL
                    ORDER BY name";

                IEnumerable<DirectoryEntity> entities = await dbSession.Connection.QueryAsync<DirectoryEntity>(
                    query, new { parentId }, transaction: dbSession.Transaction);

                return (IReadOnlyList<DirectoryRecord>)entities.Select(entity => entity.ToDomain()).ToList();
            }, session);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<string>> GetPathNamesAsync(Guid id, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $@"
                    WITH RECURSIVE ancestor (id, parent_id, name, depth) AS (
                        SELECT id, parent_id, name, 0 FROM directory WHERE id = @{nameof(id)}
                        UNION ALL
                        SELECT d.id, d.parent_id, d.name, a.depth + 1
                        FROM directory d
                        JOIN ancestor a ON d.id = a.parent_id
                    )
                    SELECT name AS {nameof(NameEntity.Name)}
                    FROM ancestor
                    WHERE parent_id IS NOT NULL
                    ORDER BY depth DESC";

                IEnumerable<NameEntity> names = await dbSession.Connection.QueryAsync<NameEntity>(
                    query, new { id }, transaction: dbSession.Transaction);

                return (IReadOnlyList<string>)names.Select(entity => entity.Name).ToList();
            }, session);
        }

        /// <inheritdoc />
        public async Task InsertAsync(DirectoryRecord directory, IDbSession session)
        {
            string query = @"
                INSERT INTO directory (id, shelf_id, parent_id, name, created_at, updated_at, deleted_at, deletion_id)
                VALUES (@Id, @ShelfId, @ParentId, @Name, @CreatedAt, @UpdatedAt, @DeletedAt, @DeletionId)";

            await session.Connection.ExecuteAsync(
                query,
                new
                {
                    directory.Id,
                    directory.ShelfId,
                    directory.ParentId,
                    directory.Name,
                    directory.CreatedAt,
                    directory.UpdatedAt,
                    directory.DeletedAt,
                    directory.DeletionId,
                },
                transaction: session.Transaction);

            await _changeLog.AppendAsync(ChangeEntityType.Directory, directory.Id, ChangeAction.Added, directory, directory.CreatedAt, session);
        }

        /// <inheritdoc />
        public async Task<int> RestoreDeletionAsync(Guid deletionId, DateTimeOffset restoredAt, IDbSession session)
        {
            string query = $@"
                UPDATE directory
                SET deleted_at = NULL, deletion_id = NULL, updated_at = @{nameof(restoredAt)}
                WHERE deletion_id = @{nameof(deletionId)}
                RETURNING {SelectColumns}";

            List<DirectoryEntity> restored = (await session.Connection.QueryAsync<DirectoryEntity>(
                query, new { deletionId, restoredAt }, transaction: session.Transaction)).ToList();

            foreach (DirectoryEntity entity in restored)
            {
                await _changeLog.AppendAsync(ChangeEntityType.Directory, entity.Id, ChangeAction.Restored, entity.ToDomain(), restoredAt, session);
            }

            return restored.Count;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<DirectoryRecord>> GetPurgeableBeforeAsync(DateTimeOffset deletedBefore, int limit, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $@"
                    SELECT {SelectColumns}
                    FROM directory d
                    WHERE d.deleted_at IS NOT NULL AND d.deleted_at < @{nameof(deletedBefore)}
                      AND NOT EXISTS (SELECT 1 FROM directory child WHERE child.parent_id = d.id)
                      AND NOT EXISTS (SELECT 1 FROM file f WHERE f.directory_id = d.id)
                    ORDER BY d.deleted_at
                    LIMIT @{nameof(limit)}";

                IEnumerable<DirectoryEntity> entities = await dbSession.Connection.QueryAsync<DirectoryEntity>(
                    query, new { deletedBefore, limit }, transaction: dbSession.Transaction);

                return (IReadOnlyList<DirectoryRecord>)entities.Select(entity => entity.ToDomain()).ToList();
            }, session);
        }

        /// <inheritdoc />
        public async Task<bool> PurgeAsync(Guid id, DateTimeOffset purgedAt, IDbSession session)
        {
            string query = $"DELETE FROM directory WHERE id = @{nameof(id)} AND deleted_at IS NOT NULL";

            int rowsAffected = await session.Connection.ExecuteAsync(
                query, new { id }, transaction: session.Transaction);

            if (rowsAffected == 0)
            {
                return false;
            }

            await _changeLog.AppendAsync<DirectoryRecord>(ChangeEntityType.Directory, id, ChangeAction.Purged, null, purgedAt, session);
            return true;
        }

        internal class NameEntity
        {
            public string Name { get; set; } = string.Empty;
        }

        internal class DirectoryEntity
        {
            public Guid Id { get; set; }
            public Guid ShelfId { get; set; }
            public Guid? ParentId { get; set; }
            public string Name { get; set; } = string.Empty;
            public DateTimeOffset CreatedAt { get; set; }
            public DateTimeOffset UpdatedAt { get; set; }
            public DateTimeOffset? DeletedAt { get; set; }
            public Guid? DeletionId { get; set; }

            public DirectoryRecord ToDomain()
            {
                return new DirectoryRecord
                {
                    Id = Id,
                    ShelfId = ShelfId,
                    ParentId = ParentId,
                    Name = Name,
                    CreatedAt = CreatedAt,
                    UpdatedAt = UpdatedAt,
                    DeletedAt = DeletedAt,
                    DeletionId = DeletionId,
                };
            }
        }
    }
}
