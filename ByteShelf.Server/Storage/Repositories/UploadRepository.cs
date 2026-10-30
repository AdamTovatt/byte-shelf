using System.Data.Common;
using ByteShelf.Server.Storage.Models;
using EasyReasy.Database;
using EasyReasy.Database.Mapping;

namespace ByteShelf.Server.Storage.Repositories
{
    public class UploadRepository : RepositoryBase, IUploadRepository
    {
        private const string SelectColumns = $@"
            id,
            shelf_id AS {nameof(UploadEntity.ShelfId)},
            path,
            content_type AS {nameof(UploadEntity.ContentType)},
            size_bytes AS {nameof(UploadEntity.SizeBytes)},
            received_bytes AS {nameof(UploadEntity.ReceivedBytes)},
            overwrite,
            is_resumable AS {nameof(UploadEntity.IsResumable)},
            created_at AS {nameof(UploadEntity.CreatedAt)},
            last_activity_at AS {nameof(UploadEntity.LastActivityAt)}";

        public UploadRepository(DbDataSource dataSource, IDbSessionFactory sessionFactory)
            : base(dataSource, sessionFactory)
        {
        }

        /// <inheritdoc />
        public async Task<UploadRecord?> GetByIdAsync(Guid id, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $"SELECT {SelectColumns} FROM upload WHERE id = @{nameof(id)}";

                UploadEntity? entity = await dbSession.Connection.QuerySingleOrDefaultAsync<UploadEntity>(
                    query, new { id }, transaction: dbSession.Transaction);

                return entity?.ToDomain();
            }, session);
        }

        /// <inheritdoc />
        public async Task InsertAsync(UploadRecord upload, IDbSession session)
        {
            string query = @"
                INSERT INTO upload (id, shelf_id, path, content_type, size_bytes, received_bytes, overwrite, is_resumable, created_at, last_activity_at)
                VALUES (@Id, @ShelfId, @Path, @ContentType, @SizeBytes, @ReceivedBytes, @Overwrite, @IsResumable, @CreatedAt, @LastActivityAt)";

            await session.Connection.ExecuteAsync(
                query,
                new
                {
                    upload.Id,
                    upload.ShelfId,
                    upload.Path,
                    upload.ContentType,
                    upload.SizeBytes,
                    upload.ReceivedBytes,
                    upload.Overwrite,
                    upload.IsResumable,
                    upload.CreatedAt,
                    upload.LastActivityAt,
                },
                transaction: session.Transaction);
        }

        /// <inheritdoc />
        public async Task<bool> UpdateReceivedAsync(Guid id, long expectedReceivedBytes, long receivedBytes, DateTimeOffset activityAt, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $@"
                    UPDATE upload
                    SET received_bytes = @{nameof(receivedBytes)}, last_activity_at = @{nameof(activityAt)}
                    WHERE id = @{nameof(id)} AND received_bytes = @{nameof(expectedReceivedBytes)}";

                int rowsAffected = await dbSession.Connection.ExecuteAsync(
                    query, new { id, expectedReceivedBytes, receivedBytes, activityAt }, transaction: dbSession.Transaction);

                return rowsAffected > 0;
            }, session);
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(Guid id, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $"DELETE FROM upload WHERE id = @{nameof(id)}";

                int rowsAffected = await dbSession.Connection.ExecuteAsync(
                    query, new { id }, transaction: dbSession.Transaction);

                return rowsAffected > 0;
            }, session);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<UploadRecord>> GetAbandonedAsync(DateTimeOffset idleBefore, DateTimeOffset? singlePutsStartedBefore, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $@"
                    SELECT {SelectColumns}
                    FROM upload
                    WHERE last_activity_at < @{nameof(idleBefore)}
                       OR (is_resumable = 0 AND created_at < @{nameof(singlePutsStartedBefore)})";

                IEnumerable<UploadEntity> entities = await dbSession.Connection.QueryAsync<UploadEntity>(
                    query, new { idleBefore, singlePutsStartedBefore }, transaction: dbSession.Transaction);

                return (IReadOnlyList<UploadRecord>)entities.Select(entity => entity.ToDomain()).ToList();
            }, session);
        }

        /// <inheritdoc />
        public async Task<IReadOnlySet<Guid>> GetAllIdsAsync(IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                IEnumerable<IdEntity> entities = await dbSession.Connection.QueryAsync<IdEntity>(
                    "SELECT id FROM upload", transaction: dbSession.Transaction);

                return (IReadOnlySet<Guid>)entities.Select(entity => entity.Id).ToHashSet();
            }, session);
        }

        internal class IdEntity
        {
            public Guid Id { get; set; }
        }

        internal class UploadEntity
        {
            public Guid Id { get; set; }
            public Guid ShelfId { get; set; }
            public string Path { get; set; } = string.Empty;
            public string ContentType { get; set; } = string.Empty;
            public long SizeBytes { get; set; }
            public long ReceivedBytes { get; set; }
            public bool Overwrite { get; set; }
            public bool IsResumable { get; set; }
            public DateTimeOffset CreatedAt { get; set; }
            public DateTimeOffset LastActivityAt { get; set; }

            public UploadRecord ToDomain()
            {
                return new UploadRecord
                {
                    Id = Id,
                    ShelfId = ShelfId,
                    Path = Path,
                    ContentType = ContentType,
                    SizeBytes = SizeBytes,
                    ReceivedBytes = ReceivedBytes,
                    Overwrite = Overwrite,
                    IsResumable = IsResumable,
                    CreatedAt = CreatedAt,
                    LastActivityAt = LastActivityAt,
                };
            }
        }
    }
}
