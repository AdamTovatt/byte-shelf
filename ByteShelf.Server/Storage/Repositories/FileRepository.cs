using System.Data.Common;
using System.Text.Json;
using ByteShelf.Server.ChangeLog.Models;
using ByteShelf.Server.ChangeLog.Repositories;
using ByteShelf.Server.Shared.Database;
using ByteShelf.Server.Storage.Models;
using ByteShelfCommon;
using EasyReasy.Database;
using EasyReasy.Database.Mapping;

namespace ByteShelf.Server.Storage.Repositories
{
    public class FileRepository : RepositoryBase, IFileRepository
    {
        private const string SelectColumns = $@"
            id,
            shelf_id AS {nameof(FileEntity.ShelfId)},
            directory_id AS {nameof(FileEntity.DirectoryId)},
            name,
            content_type AS {nameof(FileEntity.ContentType)},
            size_bytes AS {nameof(FileEntity.SizeBytes)},
            sha256,
            created_at AS {nameof(FileEntity.CreatedAt)},
            updated_at AS {nameof(FileEntity.UpdatedAt)},
            description,
            deleted_at AS {nameof(FileEntity.DeletedAt)},
            deletion_id AS {nameof(FileEntity.DeletionId)}";

        private const string InsertSql = @"
            INSERT INTO file (id, shelf_id, directory_id, name, content_type, size_bytes, sha256, created_at, updated_at, description, deleted_at, deletion_id, name_fold, description_fold, media_type)
            VALUES (@Id, @ShelfId, @DirectoryId, @Name, @ContentType, @SizeBytes, @Sha256, @CreatedAt, @UpdatedAt, @Description, @DeletedAt, @DeletionId, @NameFold, @DescriptionFold, @MediaType)";

        private static readonly string UpsertReplicatedSql = UpsertSql.OnIdConflict(InsertSql);

        private readonly IChangeLogRepository _changeLog;

        public FileRepository(DbDataSource dataSource, IDbSessionFactory sessionFactory, IChangeLogRepository changeLog)
            : base(dataSource, sessionFactory)
        {
            _changeLog = changeLog;
        }

        /// <inheritdoc />
        public async Task<FileRecord?> GetByIdAsync(Guid id, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $"SELECT {SelectColumns} FROM file WHERE id = @{nameof(id)}";

                FileEntity? entity = await dbSession.Connection.QuerySingleOrDefaultAsync<FileEntity>(
                    query, new { id }, transaction: dbSession.Transaction);

                return entity?.ToDomain();
            }, session);
        }

        /// <inheritdoc />
        public async Task<FileRecord?> GetLiveByNameAsync(Guid directoryId, string name, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $@"
                    SELECT {SelectColumns}
                    FROM file
                    WHERE directory_id = @{nameof(directoryId)} AND name = @{nameof(name)} AND deleted_at IS NULL";

                FileEntity? entity = await dbSession.Connection.QuerySingleOrDefaultAsync<FileEntity>(
                    query, new { directoryId, name }, transaction: dbSession.Transaction);

                return entity?.ToDomain();
            }, session);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<FileRecord>> GetLiveInDirectoryAsync(Guid directoryId, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $@"
                    SELECT {SelectColumns}
                    FROM file
                    WHERE directory_id = @{nameof(directoryId)} AND deleted_at IS NULL
                    ORDER BY name";

                IEnumerable<FileEntity> entities = await dbSession.Connection.QueryAsync<FileEntity>(
                    query, new { directoryId }, transaction: dbSession.Transaction);

                return (IReadOnlyList<FileRecord>)entities.Select(entity => entity.ToDomain()).ToList();
            }, session);
        }

        /// <inheritdoc />
        public async Task InsertAsync(FileRecord file, IDbSession session)
        {
            await session.Connection.ExecuteAsync(InsertSql, Parameters(file), transaction: session.Transaction);
            await _changeLog.AppendAsync(ChangeEntityType.File, file.Id, ChangeAction.Added, file, file.CreatedAt, session);
        }

        /// <inheritdoc />
        public async Task<bool> SoftDeleteAsync(Guid id, Guid deletionId, DateTimeOffset deletedAt, IDbSession session)
        {
            string query = $@"
                UPDATE file
                SET deleted_at = @{nameof(deletedAt)}, deletion_id = @{nameof(deletionId)}, updated_at = @{nameof(deletedAt)}
                WHERE id = @{nameof(id)} AND deleted_at IS NULL
                RETURNING {SelectColumns}";

            FileEntity? deleted = await session.Connection.QuerySingleOrDefaultAsync<FileEntity>(
                query, new { id, deletionId, deletedAt }, transaction: session.Transaction);

            if (deleted == null)
            {
                return false;
            }

            await _changeLog.AppendAsync(ChangeEntityType.File, id, ChangeAction.Deleted, deleted.ToDomain(), deletedAt, session);
            return true;
        }

        /// <inheritdoc />
        public async Task<int> SoftDeleteInDirectoriesAsync(IReadOnlyList<Guid> directoryIds, Guid deletionId, DateTimeOffset deletedAt, IDbSession session)
        {
            // One JSON array rather than a parameter per id, which a large subtree would run out of. The
            // ids are written as the Guid type handler stores them.
            string directoryIdsJson = JsonSerializer.Serialize(directoryIds.Select(id => id.ToString("D")));

            string query = $@"
                UPDATE file
                SET deleted_at = @{nameof(deletedAt)}, deletion_id = @{nameof(deletionId)}, updated_at = @{nameof(deletedAt)}
                WHERE deleted_at IS NULL AND directory_id IN (SELECT value FROM json_each(@{nameof(directoryIdsJson)}))
                RETURNING {SelectColumns}";

            List<FileEntity> deleted = (await session.Connection.QueryAsync<FileEntity>(
                query, new { directoryIdsJson, deletionId, deletedAt }, transaction: session.Transaction)).ToList();

            foreach (FileEntity file in deleted)
            {
                await _changeLog.AppendAsync(ChangeEntityType.File, file.Id, ChangeAction.Deleted, file.ToDomain(), deletedAt, session);
            }

            return deleted.Count;
        }

        /// <inheritdoc />
        public async Task<FileRecord?> MoveAsync(Guid id, Guid directoryId, string name, DateTimeOffset movedAt, IDbSession session)
        {
            string nameFold = NameFold(name);
            string query = $@"
                UPDATE file
                SET directory_id = @{nameof(directoryId)}, name = @{nameof(name)}, name_fold = @{nameof(nameFold)}, updated_at = @{nameof(movedAt)}
                WHERE id = @{nameof(id)} AND deleted_at IS NULL
                RETURNING {SelectColumns}";

            FileEntity? moved = await session.Connection.QuerySingleOrDefaultAsync<FileEntity>(
                query, new { id, directoryId, name, nameFold, movedAt }, transaction: session.Transaction);

            if (moved == null)
            {
                return null;
            }

            FileRecord record = moved.ToDomain();
            await _changeLog.AppendAsync(ChangeEntityType.File, id, ChangeAction.Moved, record, movedAt, session);
            return record;
        }

        /// <inheritdoc />
        public async Task<FileRecord?> SetDescriptionAsync(Guid id, string? description, DateTimeOffset at, IDbSession session)
        {
            string? descriptionFold = DescriptionFold(description);
            string query = $@"
                UPDATE file
                SET description = @{nameof(description)}, description_fold = @{nameof(descriptionFold)}, updated_at = @{nameof(at)}
                WHERE id = @{nameof(id)} AND deleted_at IS NULL
                RETURNING {SelectColumns}";

            FileEntity? updated = await session.Connection.QuerySingleOrDefaultAsync<FileEntity>(
                query, new { id, description, descriptionFold, at }, transaction: session.Transaction);

            if (updated == null)
            {
                return null;
            }

            FileRecord record = updated.ToDomain();
            await _changeLog.AppendAsync(ChangeEntityType.File, id, ChangeAction.Updated, record, at, session);
            return record;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<(FileRecord File, string Path)>> GetLiveSubtreeAsync(Guid directoryId, string directoryPath, int? maxDepth, string? after, int maxRows, IDbSession? session = null)
        {
            SubtreeWalk walk = SubtreeWalk.ForFiles(directoryId, directoryPath, maxDepth);

            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $@"
                    {SubtreeWalk.Sql}
                    SELECT {SelectColumns}, tree.node_path || '/' || file.name AS {nameof(FileSearchEntity.FullPath)}
                    FROM file JOIN tree ON file.directory_id = tree.node_id
                    WHERE file.deleted_at IS NULL
                      AND (@{nameof(after)} IS NULL OR {nameof(FileSearchEntity.FullPath)} > @{nameof(after)})
                    ORDER BY {nameof(FileSearchEntity.FullPath)}
                    LIMIT @{nameof(maxRows)}";

                IEnumerable<FileSearchEntity> entities = await dbSession.Connection.QueryAsync<FileSearchEntity>(
                    query, new { walk.DirectoryId, walk.BasePath, walk.MaxLevel, after, maxRows }, transaction: dbSession.Transaction);

                return (IReadOnlyList<(FileRecord, string)>)entities.Select(entity => (entity.ToDomain(), entity.FullPath)).ToList();
            }, session);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<(FileRecord File, string Path)>> SearchAsync(Guid directoryId, string directoryPath, FileSearch search, int maxRows, IDbSession? session = null)
        {
            SubtreeWalk walk = SubtreeWalk.ForFiles(directoryId, directoryPath, depth: null);
            string? text = search.Text;
            string? mediaType = search.ExactMediaType();
            string? mediaPrefix = search.MediaTypePrefix();
            long? minSize = search.MinSizeBytes;
            long? maxSize = search.MaxSizeBytes;
            DateTimeOffset? createdFrom = search.CreatedFrom;
            DateTimeOffset? createdBefore = search.CreatedBefore;
            DateTimeOffset? updatedFrom = search.UpdatedFrom;
            DateTimeOffset? updatedBefore = search.UpdatedBefore;
            string? sha256 = search.Sha256;
            string? after = search.After;

            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $@"
                    {SubtreeWalk.Sql}
                    SELECT {SelectColumns}, tree.node_path || '/' || file.name AS {nameof(FileSearchEntity.FullPath)}
                    FROM file JOIN tree ON file.directory_id = tree.node_id
                    WHERE file.deleted_at IS NULL
                      AND (@{nameof(text)} IS NULL
                           OR instr(file.name_fold, @{nameof(text)}) > 0
                           OR instr(coalesce(file.description_fold, ''), @{nameof(text)}) > 0)
                      AND (@{nameof(mediaType)} IS NULL OR file.media_type = @{nameof(mediaType)})
                      AND (@{nameof(mediaPrefix)} IS NULL
                           OR substr(file.media_type, 1, length(@{nameof(mediaPrefix)})) = @{nameof(mediaPrefix)})
                      AND (@{nameof(minSize)} IS NULL OR file.size_bytes >= @{nameof(minSize)})
                      AND (@{nameof(maxSize)} IS NULL OR file.size_bytes <= @{nameof(maxSize)})
                      AND (@{nameof(createdFrom)} IS NULL OR file.created_at >= @{nameof(createdFrom)})
                      AND (@{nameof(createdBefore)} IS NULL OR file.created_at < @{nameof(createdBefore)})
                      AND (@{nameof(updatedFrom)} IS NULL OR file.updated_at >= @{nameof(updatedFrom)})
                      AND (@{nameof(updatedBefore)} IS NULL OR file.updated_at < @{nameof(updatedBefore)})
                      AND (@{nameof(sha256)} IS NULL OR file.sha256 = @{nameof(sha256)})
                      AND (@{nameof(after)} IS NULL OR {nameof(FileSearchEntity.FullPath)} > @{nameof(after)})
                    ORDER BY {nameof(FileSearchEntity.FullPath)}
                    LIMIT @{nameof(maxRows)}";

                IEnumerable<FileSearchEntity> entities = await dbSession.Connection.QueryAsync<FileSearchEntity>(
                    query,
                    new { walk.DirectoryId, walk.BasePath, walk.MaxLevel, text, mediaType, mediaPrefix, minSize, maxSize, createdFrom, createdBefore, updatedFrom, updatedBefore, sha256, after, maxRows },
                    transaction: dbSession.Transaction);

                return (IReadOnlyList<(FileRecord, string)>)entities.Select(entity => (entity.ToDomain(), entity.FullPath)).ToList();
            }, session);
        }

        /// <inheritdoc />
        public async Task<int> RestoreDeletionAsync(Guid deletionId, DateTimeOffset restoredAt, IDbSession session)
        {
            string query = $@"
                UPDATE file
                SET deleted_at = NULL, deletion_id = NULL, updated_at = @{nameof(restoredAt)}
                WHERE deletion_id = @{nameof(deletionId)}
                RETURNING {SelectColumns}";

            List<FileEntity> restored = (await session.Connection.QueryAsync<FileEntity>(
                query, new { deletionId, restoredAt }, transaction: session.Transaction)).ToList();

            foreach (FileEntity entity in restored)
            {
                await _changeLog.AppendAsync(ChangeEntityType.File, entity.Id, ChangeAction.Restored, entity.ToDomain(), restoredAt, session);
            }

            return restored.Count;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<FileRecord>> GetTrashedBeforeAsync(DateTimeOffset deletedBefore, Guid? shelfId, int limit, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $@"
                    SELECT {SelectColumns}
                    FROM file
                    WHERE deleted_at IS NOT NULL AND deleted_at < @{nameof(deletedBefore)}
                      AND (@{nameof(shelfId)} IS NULL OR shelf_id = @{nameof(shelfId)})
                    ORDER BY deleted_at
                    LIMIT @{nameof(limit)}";

                IEnumerable<FileEntity> entities = await dbSession.Connection.QueryAsync<FileEntity>(
                    query, new { deletedBefore, shelfId, limit }, transaction: dbSession.Transaction);

                return (IReadOnlyList<FileRecord>)entities.Select(entity => entity.ToDomain()).ToList();
            }, session);
        }

        /// <inheritdoc />
        public async Task<bool> PurgeAsync(Guid id, DateTimeOffset purgedAt, IDbSession session)
        {
            string query = $"DELETE FROM file WHERE id = @{nameof(id)} AND deleted_at IS NOT NULL";

            int rowsAffected = await session.Connection.ExecuteAsync(
                query, new { id }, transaction: session.Transaction);

            if (rowsAffected == 0)
            {
                return false;
            }

            await _changeLog.AppendAsync<FileRecord>(ChangeEntityType.File, id, ChangeAction.Purged, null, purgedAt, session);
            return true;
        }

        /// <inheritdoc />
        public async Task UpsertReplicatedAsync(FileRecord file, string action, DateTimeOffset at, IDbSession session)
        {
            if (file.DeletedAt == null)
            {
                FileRecord? holder = await GetLiveByNameAsync(file.DirectoryId, file.Name, session);
                if (holder != null && holder.Id != file.Id)
                {
                    await SoftDeleteAsync(holder.Id, Guid.NewGuid(), at, session);
                }
            }

            await session.Connection.ExecuteAsync(UpsertReplicatedSql, Parameters(file), transaction: session.Transaction);
            await _changeLog.AppendAsync(ChangeEntityType.File, file.Id, action, file, at, session);
        }

        /// <inheritdoc />
        public async Task PurgeForShelfAsync(Guid shelfId, DateTimeOffset purgedAt, IDbSession session)
        {
            string query = $"DELETE FROM file WHERE shelf_id = @{nameof(shelfId)} RETURNING id";
            await _changeLog.DeleteAndLogAsync(ChangeEntityType.File, query, new { shelfId }, purgedAt, session);
        }

        /// <inheritdoc />
        public async Task<bool> IsObjectReferencedAsync(string sha256, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $"SELECT EXISTS (SELECT 1 FROM file WHERE sha256 = @{nameof(sha256)})";

                long exists = await dbSession.Connection.QuerySingleAsync<long>(
                    query, new { sha256 }, transaction: dbSession.Transaction);

                return exists != 0;
            }, session);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<string>> GetReferencedObjectsAsync(IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                IEnumerable<string> hashes = await dbSession.Connection.QueryAsync<string>(
                    "SELECT DISTINCT sha256 FROM file ORDER BY sha256", transaction: dbSession.Transaction);

                return (IReadOnlyList<string>)hashes.ToList();
            }, session);
        }

        /// <inheritdoc />
        public async Task<int> PurgeAllWithObjectAsync(string sha256, DateTimeOffset purgedAt, IDbSession session)
        {
            string query = $"DELETE FROM file WHERE sha256 = @{nameof(sha256)} RETURNING id";
            return await _changeLog.DeleteAndLogAsync(ChangeEntityType.File, query, new { sha256 }, purgedAt, session);
        }

        private static object Parameters(FileRecord file)
        {
            return new
            {
                file.Id,
                file.ShelfId,
                file.DirectoryId,
                file.Name,
                file.ContentType,
                file.SizeBytes,
                file.Sha256,
                file.CreatedAt,
                file.UpdatedAt,
                file.Description,
                file.DeletedAt,
                file.DeletionId,
                NameFold = NameFold(file.Name),
                DescriptionFold = DescriptionFold(file.Description),
                MediaType = ContentTypes.MediaTypeOf(file.ContentType),
            };
        }

        // The columns search compares (see v0006) are only ever written through these and Parameters, so
        // each always holds what Common's rules make of the row's own name, description and type.
        private static string NameFold(string name)
        {
            return FileSearch.Fold(name);
        }

        private static string? DescriptionFold(string? description)
        {
            return description == null ? null : FileSearch.Fold(description);
        }

        internal sealed class FileSearchEntity : FileEntity
        {
            public string FullPath { get; set; } = string.Empty;
        }

        internal class FileEntity
        {
            public Guid Id { get; set; }
            public Guid ShelfId { get; set; }
            public Guid DirectoryId { get; set; }
            public string Name { get; set; } = string.Empty;
            public string ContentType { get; set; } = string.Empty;
            public long SizeBytes { get; set; }
            public string Sha256 { get; set; } = string.Empty;
            public DateTimeOffset CreatedAt { get; set; }
            public DateTimeOffset UpdatedAt { get; set; }
            public string? Description { get; set; }
            public DateTimeOffset? DeletedAt { get; set; }
            public Guid? DeletionId { get; set; }

            public FileRecord ToDomain()
            {
                return new FileRecord
                {
                    Id = Id,
                    ShelfId = ShelfId,
                    DirectoryId = DirectoryId,
                    Name = Name,
                    ContentType = ContentType,
                    SizeBytes = SizeBytes,
                    Sha256 = Sha256,
                    CreatedAt = CreatedAt,
                    UpdatedAt = UpdatedAt,
                    Description = Description,
                    DeletedAt = DeletedAt,
                    DeletionId = DeletionId,
                };
            }
        }
    }
}
