using System.Data.Common;
using ByteShelf.Server.ChangeLog.Models;
using ByteShelf.Server.ChangeLog.Repositories;
using ByteShelf.Server.Shared.Database;
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

        private const string InsertSql = @"
            INSERT INTO directory (id, shelf_id, parent_id, name, created_at, updated_at, deleted_at, deletion_id)
            VALUES (@Id, @ShelfId, @ParentId, @Name, @CreatedAt, @UpdatedAt, @DeletedAt, @DeletionId)";

        private static readonly string UpsertReplicatedSql = UpsertSql.OnIdConflict(InsertSql);

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
            await session.Connection.ExecuteAsync(InsertSql, Parameters(directory), transaction: session.Transaction);
            await _changeLog.AppendAsync(ChangeEntityType.Directory, directory.Id, ChangeAction.Added, directory, directory.CreatedAt, session);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<string>> GetRelativePathsLongerThanAsync(Guid id, int utf8Bytes, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                // SQLite measures text in code points and bytes, not in UTF-16 units. A path's UTF-8 byte
                // count is never below its UTF-16 length, so every path that could be too long is
                // answered, and the caller measures them exactly.
                string query = $@"
                    WITH RECURSIVE tree (id, path) AS (
                        SELECT id, '' FROM directory WHERE id = @{nameof(id)}
                        UNION ALL
                        SELECT d.id, t.path || '/' || d.name FROM directory d JOIN tree t ON d.parent_id = t.id
                    ),
                    entry (path) AS (
                        SELECT path FROM tree
                        UNION ALL
                        SELECT t.path || '/' || f.name FROM file f JOIN tree t ON f.directory_id = t.id
                    )
                    SELECT path AS {nameof(PathEntity.Path)} FROM entry WHERE length(CAST(path AS BLOB)) > @{nameof(utf8Bytes)}";

                IEnumerable<PathEntity> paths = await dbSession.Connection.QueryAsync<PathEntity>(
                    query, new { id, utf8Bytes }, transaction: dbSession.Transaction);

                return (IReadOnlyList<string>)paths.Select(entity => entity.Path).ToList();
            }, session);
        }

        /// <inheritdoc />
        public async Task<DirectoryRecord?> MoveAsync(Guid id, Guid parentId, string name, DateTimeOffset movedAt, IDbSession session)
        {
            string query = $@"
                UPDATE directory
                SET parent_id = @{nameof(parentId)}, name = @{nameof(name)}, updated_at = @{nameof(movedAt)}
                WHERE id = @{nameof(id)} AND parent_id IS NOT NULL AND deleted_at IS NULL
                RETURNING {SelectColumns}";

            DirectoryEntity? moved = await session.Connection.QuerySingleOrDefaultAsync<DirectoryEntity>(
                query, new { id, parentId, name, movedAt }, transaction: session.Transaction);

            if (moved == null)
            {
                return null;
            }

            DirectoryRecord record = moved.ToDomain();
            await _changeLog.AppendAsync(ChangeEntityType.Directory, id, ChangeAction.Moved, record, movedAt, session);
            return record;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<Guid>> SoftDeleteSubtreeAsync(Guid id, Guid deletionId, DateTimeOffset deletedAt, IDbSession session)
        {
            // Only live rows are taken: anything under it already in the trash stays in its own
            // deletion, so restoring either one brings back exactly what it took.
            string query = $@"
                WITH RECURSIVE subtree (id) AS (
                    SELECT id FROM directory WHERE id = @{nameof(id)} AND parent_id IS NOT NULL AND deleted_at IS NULL
                    UNION ALL
                    SELECT d.id FROM directory d JOIN subtree s ON d.parent_id = s.id WHERE d.deleted_at IS NULL
                )
                UPDATE directory
                SET deleted_at = @{nameof(deletedAt)}, deletion_id = @{nameof(deletionId)}, updated_at = @{nameof(deletedAt)}
                WHERE id IN (SELECT id FROM subtree)
                RETURNING {SelectColumns}";

            List<DirectoryEntity> directories = (await session.Connection.QueryAsync<DirectoryEntity>(
                query, new { id, deletionId, deletedAt }, transaction: session.Transaction)).ToList();

            foreach (DirectoryEntity directory in directories)
            {
                await _changeLog.AppendAsync(ChangeEntityType.Directory, directory.Id, ChangeAction.Deleted, directory.ToDomain(), deletedAt, session);
            }

            return directories.Select(directory => directory.Id).ToList();
        }

        /// <inheritdoc />
        public async Task DeleteRootAsync(Guid shelfId, DateTimeOffset deletedAt, IDbSession session)
        {
            string query = $"DELETE FROM directory WHERE shelf_id = @{nameof(shelfId)} AND parent_id IS NULL RETURNING id";

            Guid rootId = await session.Connection.QuerySingleAsync<Guid>(
                query, new { shelfId }, transaction: session.Transaction);

            await _changeLog.AppendAsync<DirectoryRecord>(ChangeEntityType.Directory, rootId, ChangeAction.Purged, null, deletedAt, session);
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
        public async Task<IReadOnlyList<DirectoryRecord>> GetPurgeableBeforeAsync(DateTimeOffset deletedBefore, Guid? shelfId, int limit, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $@"
                    SELECT {SelectColumns}
                    FROM directory d
                    WHERE d.deleted_at IS NOT NULL AND d.deleted_at < @{nameof(deletedBefore)}
                      AND (@{nameof(shelfId)} IS NULL OR d.shelf_id = @{nameof(shelfId)})
                      AND NOT EXISTS (SELECT 1 FROM directory child WHERE child.parent_id = d.id)
                      AND NOT EXISTS (SELECT 1 FROM file f WHERE f.directory_id = d.id)
                    ORDER BY d.deleted_at
                    LIMIT @{nameof(limit)}";

                IEnumerable<DirectoryEntity> entities = await dbSession.Connection.QueryAsync<DirectoryEntity>(
                    query, new { deletedBefore, shelfId, limit }, transaction: dbSession.Transaction);

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

        /// <inheritdoc />
        public async Task UpsertReplicatedAsync(DirectoryRecord directory, string action, DateTimeOffset at, IDbSession session)
        {
            if (directory.DeletedAt == null && directory.ParentId is Guid parentId)
            {
                DirectoryRecord? holder = await GetLiveChildAsync(parentId, directory.Name, session);
                if (holder != null && holder.Id != directory.Id)
                {
                    await TrashIfLiveAsync(holder.Id, at, session);
                }
            }

            await session.Connection.ExecuteAsync(UpsertReplicatedSql, Parameters(directory), transaction: session.Transaction);
            await _changeLog.AppendAsync(ChangeEntityType.Directory, directory.Id, action, directory, at, session);
        }

        /// <inheritdoc />
        public async Task<bool> TrashIfLiveAsync(Guid id, DateTimeOffset at, IDbSession session)
        {
            Guid deletionId = Guid.NewGuid();
            string query = $@"
                UPDATE directory
                SET deleted_at = @{nameof(at)}, deletion_id = @{nameof(deletionId)}, updated_at = @{nameof(at)}
                WHERE id = @{nameof(id)} AND deleted_at IS NULL AND parent_id IS NOT NULL
                RETURNING {SelectColumns}";

            DirectoryEntity? trashed = await session.Connection.QuerySingleOrDefaultAsync<DirectoryEntity>(
                query, new { id, at, deletionId }, transaction: session.Transaction);

            if (trashed == null)
            {
                return false;
            }

            await _changeLog.AppendAsync(ChangeEntityType.Directory, id, ChangeAction.Deleted, trashed.ToDomain(), at, session);
            return true;
        }

        /// <inheritdoc />
        public async Task PurgeForShelfAsync(Guid shelfId, DateTimeOffset purgedAt, IDbSession session)
        {
            string query = $"DELETE FROM directory WHERE shelf_id = @{nameof(shelfId)} RETURNING id";
            await _changeLog.DeleteAndLogAsync(ChangeEntityType.Directory, query, new { shelfId }, purgedAt, session);
        }

        private static object Parameters(DirectoryRecord directory)
        {
            return new
            {
                directory.Id,
                directory.ShelfId,
                directory.ParentId,
                directory.Name,
                directory.CreatedAt,
                directory.UpdatedAt,
                directory.DeletedAt,
                directory.DeletionId,
            };
        }

        internal class NameEntity
        {
            public string Name { get; set; } = string.Empty;
        }

        internal class PathEntity
        {
            public string Path { get; set; } = string.Empty;
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
