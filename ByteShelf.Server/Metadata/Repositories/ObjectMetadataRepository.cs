using System.Data.Common;
using ByteShelf.Server.ChangeLog.Models;
using ByteShelf.Server.ChangeLog.Repositories;
using ByteShelf.Server.Metadata.Models;
using ByteShelf.Server.Shared.Database;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Repositories;
using ByteShelfCommon;
using EasyReasy.Database;
using EasyReasy.Database.Mapping;

namespace ByteShelf.Server.Metadata.Repositories
{
    public class ObjectMetadataRepository : RepositoryBase, IObjectMetadataRepository
    {
        private const string SelectColumns = $@"
            id,
            sha256,
            reader_version AS {nameof(ObjectMetadataEntity.ReaderVersion)},
            read_at AS {nameof(ObjectMetadataEntity.ReadAt)},
            taken_at AS {nameof(ObjectMetadataEntity.TakenAt)},
            latitude,
            longitude,
            duration_seconds AS {nameof(ObjectMetadataEntity.DurationSeconds)},
            page_count AS {nameof(ObjectMetadataEntity.PageCount)},
            title,
            camera";

        private const string Columns =
            "id, sha256, reader_version, read_at, taken_at, latitude, longitude, duration_seconds, page_count, title, title_fold, camera, camera_fold";

        private const string Values =
            "@Id, @Sha256, @ReaderVersion, @ReadAt, @TakenAt, @Latitude, @Longitude, @DurationSeconds, @PageCount, @Title, @TitleFold, @Camera, @CameraFold";

        private static readonly string UpsertReplicatedSql = UpsertSql.OnIdConflict($"INSERT INTO object_metadata ({Columns}) VALUES ({Values})");

        private static readonly string RecordSql = PlainContents.RecordSql("object_metadata", Columns, Values);

        private readonly IChangeLogRepository _changeLog;

        public ObjectMetadataRepository(DbDataSource dataSource, IDbSessionFactory sessionFactory, IChangeLogRepository changeLog)
            : base(dataSource, sessionFactory)
        {
            _changeLog = changeLog;
        }

        /// <inheritdoc />
        public async Task<ObjectMetadata?> GetBySha256Async(string sha256, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $"SELECT {SelectColumns} FROM object_metadata WHERE sha256 = @{nameof(sha256)}";

                ObjectMetadataEntity? entity = await dbSession.Connection.QuerySingleOrDefaultAsync<ObjectMetadataEntity>(
                    query, new { sha256 }, transaction: dbSession.Transaction);

                return entity?.ToDomain();
            }, session);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<string>> GetUnreadAsync(int readerVersion, MediaTypeSet mediaTypes, string? after, int limit, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = PlainContents.UnreadSql(mediaTypes, $@"
                    EXISTS (
                        SELECT 1 FROM object_metadata
                        WHERE object_metadata.sha256 = file.sha256
                          AND object_metadata.reader_version = @{nameof(readerVersion)})");

                IEnumerable<string> hashes = await dbSession.Connection.QueryAsync<string>(
                    query, new { readerVersion, after, limit }, transaction: dbSession.Transaction);

                return (IReadOnlyList<string>)hashes.ToList();
            }, session);
        }

        /// <inheritdoc />
        public async Task<bool> RecordAsync(ObjectMetadata metadata, IDbSession session)
        {
            bool isNew = await GetBySha256Async(metadata.Sha256, session) == null;
            int written = await session.Connection.ExecuteAsync(RecordSql, Parameters(metadata), transaction: session.Transaction);
            if (written == 0)
            {
                return false;
            }

            string action = isNew ? ChangeAction.Added : ChangeAction.Updated;
            await _changeLog.AppendAsync(ChangeEntityType.ObjectMetadata, metadata.Id, action, metadata, metadata.ReadAt, session);
            return true;
        }

        /// <inheritdoc />
        public async Task<int> DeleteUnheldAsync(DateTimeOffset at, IDbSession session)
        {
            return await _changeLog.DeleteAndLogAsync(ChangeEntityType.ObjectMetadata, PlainContents.DeleteUnheldSql("object_metadata"), new { }, at, session);
        }

        /// <inheritdoc />
        public async Task ReleaseUnheldByOthersAsync(string sha256, Guid fileId, DateTimeOffset at, IDbSession session)
        {
            await _changeLog.ReleaseAndLogAsync(ChangeEntityType.ObjectMetadata, PlainContents.DeleteUnheldByOthersSql("object_metadata"), new { sha256, fileId }, at, session);
        }

        /// <inheritdoc />
        public async Task UpsertReplicatedAsync(ObjectMetadata metadata, string action, DateTimeOffset at, IDbSession session)
        {
            await session.Connection.ExecuteAsync(UpsertReplicatedSql, Parameters(metadata), transaction: session.Transaction);
            await _changeLog.AppendAsync(ChangeEntityType.ObjectMetadata, metadata.Id, action, metadata, at, session);
        }

        private const string DeleteByIdSql = "DELETE FROM object_metadata WHERE id = @id RETURNING id";

        /// <inheritdoc />
        public async Task<bool> DeleteByIdAsync(Guid id, DateTimeOffset at, IDbSession session)
        {
            return await _changeLog.DeleteAndLogAsync(ChangeEntityType.ObjectMetadata, DeleteByIdSql, new { id }, at, session) > 0;
        }

        /// <inheritdoc />
        public async Task<bool> ReleaseByIdAsync(Guid id, DateTimeOffset at, IDbSession session)
        {
            return await _changeLog.ReleaseAndLogAsync(ChangeEntityType.ObjectMetadata, DeleteByIdSql, new { id }, at, session) > 0;
        }

        // The *_fold columns are only ever written here, so each holds what FileSearch.Fold makes of the
        // text beside it, as file.name_fold does of a name.
        private static object Parameters(ObjectMetadata row)
        {
            ContentMetadata metadata = row.Metadata;
            return new
            {
                row.Id,
                row.Sha256,
                row.ReaderVersion,
                row.ReadAt,
                metadata.TakenAt,
                metadata.Latitude,
                metadata.Longitude,
                metadata.DurationSeconds,
                metadata.PageCount,
                metadata.Title,
                TitleFold = metadata.Title == null ? null : FileSearch.Fold(metadata.Title),
                metadata.Camera,
                CameraFold = metadata.Camera == null ? null : FileSearch.Fold(metadata.Camera),
            };
        }

        internal sealed class ObjectMetadataEntity
        {
            public Guid Id { get; set; }
            public string Sha256 { get; set; } = string.Empty;
            public int ReaderVersion { get; set; }
            public DateTimeOffset ReadAt { get; set; }
            public DateTimeOffset? TakenAt { get; set; }
            public double? Latitude { get; set; }
            public double? Longitude { get; set; }
            public double? DurationSeconds { get; set; }
            public int? PageCount { get; set; }
            public string? Title { get; set; }
            public string? Camera { get; set; }

            public ObjectMetadata ToDomain()
            {
                return new ObjectMetadata
                {
                    Id = Id,
                    Sha256 = Sha256,
                    ReaderVersion = ReaderVersion,
                    ReadAt = ReadAt,
                    Metadata = new ContentMetadata
                    {
                        TakenAt = TakenAt,
                        Latitude = Latitude,
                        Longitude = Longitude,
                        DurationSeconds = DurationSeconds,
                        PageCount = PageCount,
                        Title = Title,
                        Camera = Camera,
                    },
                };
            }
        }
    }
}
