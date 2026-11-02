using System.Data.Common;
using ByteShelf.Server.ChangeLog.Models;
using ByteShelf.Server.ChangeLog.Repositories;
using ByteShelf.Server.Shared.Database;
using ByteShelf.Server.Storage.Models;
using ByteShelfCommon;
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
            encryption_check AS {nameof(DirectoryEntity.EncryptionCheck)},
            deleted_at AS {nameof(DirectoryEntity.DeletedAt)},
            deletion_id AS {nameof(DirectoryEntity.DeletionId)}";

        private const string InsertSql = @"
            INSERT INTO directory (id, shelf_id, parent_id, name, created_at, updated_at, encryption_check, deleted_at, deletion_id)
            VALUES (@Id, @ShelfId, @ParentId, @Name, @CreatedAt, @UpdatedAt, @EncryptionCheck, @DeletedAt, @DeletionId)";

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
        public async Task<IReadOnlyList<DirectoryWithTotals>> GetLiveChildrenAsync(Guid parentId, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                // Each child heads its own walk (top_id), so one pass sums every child's subtree. A
                // trashed directory holds only trashed entries, so it is not walked into.
                string query = $@"
                    WITH RECURSIVE tree(top_id, node_id) AS (
                        SELECT id, id FROM directory
                        WHERE parent_id = @{nameof(parentId)} AND deleted_at IS NULL
                        UNION ALL
                        SELECT tree.top_id, directory.id
                        FROM directory JOIN tree ON directory.parent_id = tree.node_id
                        WHERE directory.deleted_at IS NULL
                    ),
                    totals(top_id, size_bytes, file_count, encrypted_file_count) AS (
                        SELECT tree.top_id, COALESCE(SUM(file.size_bytes), 0), COUNT(file.id), COALESCE(SUM(file.encrypted), 0)
                        FROM tree LEFT JOIN file ON file.directory_id = tree.node_id AND file.deleted_at IS NULL
                        GROUP BY tree.top_id
                    )
                    SELECT {SelectColumns},
                        totals.size_bytes AS {nameof(DirectoryTotalsEntity.SizeBytes)},
                        totals.file_count AS {nameof(DirectoryTotalsEntity.FileCount)},
                        totals.encrypted_file_count AS {nameof(DirectoryTotalsEntity.EncryptedFileCount)}
                    FROM directory JOIN totals ON directory.id = totals.top_id
                    ORDER BY name";

                IEnumerable<DirectoryTotalsEntity> entities = await dbSession.Connection.QueryAsync<DirectoryTotalsEntity>(
                    query, new { parentId }, transaction: dbSession.Transaction);

                return (IReadOnlyList<DirectoryWithTotals>)entities.Select(entity => new DirectoryWithTotals(entity.ToDomain(), entity.SizeBytes, entity.FileCount, entity.EncryptedFileCount)).ToList();
            }, session);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<(DirectoryRecord Directory, string Path)>> GetLiveSubtreeAsync(Guid directoryId, string directoryPath, int? maxDepth, string? after, int maxRows, IDbSession? session = null)
        {
            SubtreeWalk walk = SubtreeWalk.ForDirectories(directoryId, directoryPath, maxDepth);

            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $@"
                    {SubtreeWalk.Sql}
                    SELECT {SelectColumns}, tree.node_path AS {nameof(DirectoryTreeEntity.FullPath)}
                    FROM directory JOIN tree ON directory.id = tree.node_id
                    WHERE tree.level > 0
                      AND (@{nameof(after)} IS NULL OR tree.node_path > @{nameof(after)})
                    ORDER BY tree.node_path
                    LIMIT @{nameof(maxRows)}";

                IEnumerable<DirectoryTreeEntity> entities = await dbSession.Connection.QueryAsync<DirectoryTreeEntity>(
                    query, new { walk.DirectoryId, walk.BasePath, walk.MaxLevel, after, maxRows }, transaction: dbSession.Transaction);

                return (IReadOnlyList<(DirectoryRecord, string)>)entities.Select(entity => (entity.ToDomain(), entity.FullPath)).ToList();
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
        public async Task<DirectoryRecord?> SetEncryptionCheckAsync(Guid id, byte[]? check, DateTimeOffset at, IDbSession session)
        {
            string query = $@"
                UPDATE directory
                SET encryption_check = @{nameof(check)}, updated_at = @{nameof(at)}
                WHERE id = @{nameof(id)} AND deleted_at IS NULL
                RETURNING {SelectColumns}";

            DirectoryEntity? updated = await session.Connection.QuerySingleOrDefaultAsync<DirectoryEntity>(
                query, new { id, check, at }, transaction: session.Transaction);

            if (updated == null)
            {
                return null;
            }

            DirectoryRecord record = updated.ToDomain();
            await _changeLog.AppendAsync(ChangeEntityType.Directory, id, ChangeAction.Updated, record, at, session);
            return record;
        }

        /// <inheritdoc />
        public async Task<DirectoryRecord?> GetEncryptedAncestorOrSelfAsync(Guid id, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $@"
                    WITH RECURSIVE ancestor (node_id, up_id, depth) AS (
                        SELECT id, parent_id, 0 FROM directory WHERE id = @{nameof(id)}
                        UNION ALL
                        SELECT d.id, d.parent_id, a.depth + 1 FROM directory d JOIN ancestor a ON d.id = a.up_id
                    )
                    SELECT {SelectColumns}
                    FROM directory JOIN ancestor ON directory.id = ancestor.node_id
                    WHERE directory.encryption_check IS NOT NULL
                    ORDER BY ancestor.depth
                    LIMIT 1";

                DirectoryEntity? entity = await dbSession.Connection.QuerySingleOrDefaultAsync<DirectoryEntity>(
                    query, new { id }, transaction: dbSession.Transaction);

                return entity?.ToDomain();
            }, session);
        }

        /// <inheritdoc />
        public async Task<bool> HasEncryptedLiveDescendantAsync(Guid id, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $@"
                    WITH RECURSIVE subtree (node_id) AS (
                        SELECT id FROM directory WHERE parent_id = @{nameof(id)} AND deleted_at IS NULL
                        UNION ALL
                        SELECT d.id FROM directory d JOIN subtree s ON d.parent_id = s.node_id WHERE d.deleted_at IS NULL
                    )
                    SELECT EXISTS (
                        SELECT 1 FROM directory JOIN subtree ON directory.id = subtree.node_id
                        WHERE directory.encryption_check IS NOT NULL)";

                return await dbSession.Connection.ExecuteScalarAsync<bool>(query, new { id }, transaction: dbSession.Transaction);
            }, session);
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
        public async Task<bool> DeletionHoldsEncryptedDirectoryAsync(Guid deletionId, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $"SELECT EXISTS (SELECT 1 FROM directory WHERE deletion_id = @{nameof(deletionId)} AND encryption_check IS NOT NULL)";
                return await dbSession.Connection.ExecuteScalarAsync<bool>(query, new { deletionId }, transaction: dbSession.Transaction);
            }, session);
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
        public async Task<int> ExpireTrashAsync(Guid shelfId, DateTimeOffset deletedAt, IDbSession session)
        {
            string query = $@"
                UPDATE directory
                SET deleted_at = @{nameof(deletedAt)}
                WHERE shelf_id = @{nameof(shelfId)} AND deleted_at IS NOT NULL";

            return await session.Connection.ExecuteAsync(query, new { shelfId, deletedAt }, transaction: session.Transaction);
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
        public async Task UpsertReplicatedAsync(DirectoryRecord directory, string action, Guid displacedDeletionId, DateTimeOffset at, IDbSession session)
        {
            if (directory.DeletedAt == null && directory.ParentId is Guid parentId)
            {
                DirectoryRecord? holder = await GetLiveChildAsync(parentId, directory.Name, session);
                if (holder != null && holder.Id != directory.Id)
                {
                    await TrashIfLiveAsync(holder.Id, displacedDeletionId, at, session);
                }
            }

            await session.Connection.ExecuteAsync(UpsertReplicatedSql, Parameters(directory), transaction: session.Transaction);
            await _changeLog.AppendAsync(ChangeEntityType.Directory, directory.Id, action, directory, at, session);
        }

        /// <inheritdoc />
        public async Task<bool> TrashIfLiveAsync(Guid id, Guid deletionId, DateTimeOffset at, IDbSession session)
        {
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
                directory.EncryptionCheck,
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

        internal sealed class DirectoryTreeEntity : DirectoryEntity
        {
            public string FullPath { get; set; } = string.Empty;
        }

        internal sealed class DirectoryTotalsEntity : DirectoryEntity
        {
            public long SizeBytes { get; set; }
            public int FileCount { get; set; }
            public int EncryptedFileCount { get; set; }
        }

        internal class DirectoryEntity
        {
            public Guid Id { get; set; }
            public Guid ShelfId { get; set; }
            public Guid? ParentId { get; set; }
            public string Name { get; set; } = string.Empty;
            public DateTimeOffset CreatedAt { get; set; }
            public DateTimeOffset UpdatedAt { get; set; }
            public byte[]? EncryptionCheck { get; set; }
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
                    EncryptionCheck = EncryptionCheck,
                    DeletedAt = DeletedAt,
                    DeletionId = DeletionId,
                };
            }
        }
    }
}
