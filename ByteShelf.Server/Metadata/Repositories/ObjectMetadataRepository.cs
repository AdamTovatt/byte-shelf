using System.Data.Common;
using ByteShelf.Server.ChangeLog.Models;
using ByteShelf.Server.ChangeLog.Repositories;
using ByteShelf.Server.Metadata.Models;
using ByteShelf.Server.Shared.Database;
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
            read_at AS {nameof(ObjectMetadataEntity.ReadAt)},
            taken_at AS {nameof(ObjectMetadataEntity.TakenAt)},
            latitude,
            longitude,
            duration_seconds AS {nameof(ObjectMetadataEntity.DurationSeconds)},
            page_count AS {nameof(ObjectMetadataEntity.PageCount)},
            title,
            camera";

        private const string InsertSql = @"
            INSERT INTO object_metadata (id, sha256, read_at, taken_at, latitude, longitude, duration_seconds, page_count, title, title_fold, camera, camera_fold)
            VALUES (@Id, @Sha256, @ReadAt, @TakenAt, @Latitude, @Longitude, @DurationSeconds, @PageCount, @Title, @TitleFold, @Camera, @CameraFold)";

        private static readonly string UpsertReplicatedSql = UpsertSql.OnIdConflict(InsertSql);

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
        public async Task<IReadOnlyList<string>> GetUnreadAsync(string? after, int limit, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                // The types read are a fixed list of lowercase ASCII, as media_type is, so they are written
                // into the query rather than bound.
                IEnumerable<string> typeConditions = ReadMediaTypes.Families.Select(family => $"file.media_type LIKE '{family}%'")
                    .Concat(ReadMediaTypes.Types.Select(type => $"file.media_type = '{type}'"));

                string query = $@"
                    SELECT DISTINCT file.sha256
                    FROM file
                    WHERE file.deleted_at IS NULL
                      AND file.encrypted = 0
                      AND ({string.Join(" OR ", typeConditions)})
                      AND NOT EXISTS (SELECT 1 FROM object_metadata WHERE object_metadata.sha256 = file.sha256)
                      AND (@{nameof(after)} IS NULL OR file.sha256 > @{nameof(after)})
                    ORDER BY file.sha256
                    LIMIT @{nameof(limit)}";

                IEnumerable<string> hashes = await dbSession.Connection.QueryAsync<string>(
                    query, new { after, limit }, transaction: dbSession.Transaction);

                return (IReadOnlyList<string>)hashes.ToList();
            }, session);
        }

        /// <inheritdoc />
        public async Task<bool> InsertAsync(ObjectMetadata metadata, IDbSession session)
        {
            string query = $"{InsertSql} ON CONFLICT DO NOTHING";
            int inserted = await session.Connection.ExecuteAsync(query, Parameters(metadata), transaction: session.Transaction);
            if (inserted == 0)
            {
                return false;
            }

            await _changeLog.AppendAsync(ChangeEntityType.ObjectMetadata, metadata.Id, ChangeAction.Added, metadata, metadata.ReadAt, session);
            return true;
        }

        /// <inheritdoc />
        public async Task<int> DeleteUnreferencedAsync(DateTimeOffset at, IDbSession session)
        {
            return await DeleteUnreferencedOfAsync(sha256: null, at, session);
        }

        /// <inheritdoc />
        public async Task<bool> DeleteUnreferencedAsync(string sha256, DateTimeOffset at, IDbSession session)
        {
            return await DeleteUnreferencedOfAsync(sha256, at, session) > 0;
        }

        /// <summary>Removes the rows no file holds, of the contents <paramref name="sha256"/> only, or of all when it is null.</summary>
        private async Task<int> DeleteUnreferencedOfAsync(string? sha256, DateTimeOffset at, IDbSession session)
        {
            string query = $@"
                DELETE FROM object_metadata
                WHERE (@{nameof(sha256)} IS NULL OR object_metadata.sha256 = @{nameof(sha256)})
                  AND NOT EXISTS (SELECT 1 FROM file WHERE file.sha256 = object_metadata.sha256)
                RETURNING id";

            return await _changeLog.DeleteAndLogAsync(ChangeEntityType.ObjectMetadata, query, new { sha256 }, at, session);
        }

        /// <inheritdoc />
        public async Task UpsertReplicatedAsync(ObjectMetadata metadata, string action, DateTimeOffset at, IDbSession session)
        {
            await session.Connection.ExecuteAsync(UpsertReplicatedSql, Parameters(metadata), transaction: session.Transaction);
            await _changeLog.AppendAsync(ChangeEntityType.ObjectMetadata, metadata.Id, action, metadata, at, session);
        }

        /// <inheritdoc />
        public async Task<bool> DeleteByIdAsync(Guid id, DateTimeOffset at, IDbSession session)
        {
            string query = $"DELETE FROM object_metadata WHERE id = @{nameof(id)} RETURNING id";
            return await _changeLog.DeleteAndLogAsync(ChangeEntityType.ObjectMetadata, query, new { id }, at, session) > 0;
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
