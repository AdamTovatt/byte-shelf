using System.Data.Common;
using System.Text.Json;
using ByteShelf.Server.ChangeLog.Models;
using ByteShelf.Server.ChangeLog.Repositories;
using ByteShelf.Server.Shared.Database;
using ByteShelf.Server.Storage.Models;
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
            deleted_at AS {nameof(FileEntity.DeletedAt)},
            deletion_id AS {nameof(FileEntity.DeletionId)}";

        private const string InsertSql = @"
            INSERT INTO file (id, shelf_id, directory_id, name, content_type, size_bytes, sha256, created_at, updated_at, deleted_at, deletion_id)
            VALUES (@Id, @ShelfId, @DirectoryId, @Name, @ContentType, @SizeBytes, @Sha256, @CreatedAt, @UpdatedAt, @DeletedAt, @DeletionId)";

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
            string query = $@"
                UPDATE file
                SET directory_id = @{nameof(directoryId)}, name = @{nameof(name)}, updated_at = @{nameof(movedAt)}
                WHERE id = @{nameof(id)} AND deleted_at IS NULL
                RETURNING {SelectColumns}";

            FileEntity? moved = await session.Connection.QuerySingleOrDefaultAsync<FileEntity>(
                query, new { id, directoryId, name, movedAt }, transaction: session.Transaction);

            if (moved == null)
            {
                return null;
            }

            FileRecord record = moved.ToDomain();
            await _changeLog.AppendAsync(ChangeEntityType.File, id, ChangeAction.Moved, record, movedAt, session);
            return record;
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
                file.DeletedAt,
                file.DeletionId,
            };
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
                    DeletedAt = DeletedAt,
                    DeletionId = DeletionId,
                };
            }
        }
    }
}
