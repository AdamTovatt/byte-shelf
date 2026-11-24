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
    public class ContentPluginMetadataRepository : RepositoryBase, IContentPluginMetadataRepository
    {
        private const string SelectColumns = $@"
            id,
            plugin_id AS {nameof(ContentPluginMetadataEntity.PluginId)},
            sha256,
            reader_version AS {nameof(ContentPluginMetadataEntity.ReaderVersion)},
            read_at AS {nameof(ContentPluginMetadataEntity.ReadAt)},
            document";

        private const string Columns = "id, plugin_id, sha256, reader_version, read_at, document";

        private const string Values = "@Id, @PluginId, @Sha256, @ReaderVersion, @ReadAt, @Document";

        private static readonly string UpsertReplicatedSql = UpsertSql.OnIdConflict($"INSERT INTO content_plugin_metadata ({Columns}) VALUES ({Values})");

        private static readonly string RecordSql = PlainContents.RecordSql("content_plugin_metadata", Columns, Values);

        private readonly IChangeLogRepository _changeLog;
        private readonly IPluginFieldValueRepository _fieldValues;

        public ContentPluginMetadataRepository(DbDataSource dataSource, IDbSessionFactory sessionFactory, IChangeLogRepository changeLog, IPluginFieldValueRepository fieldValues)
            : base(dataSource, sessionFactory)
        {
            _changeLog = changeLog;
            _fieldValues = fieldValues;
        }

        /// <inheritdoc />
        public async Task<ContentPluginMetadata?> GetAsync(string pluginId, string sha256, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                Guid id = ContentPluginMetadata.IdFor(pluginId, sha256);
                string query = $"SELECT {SelectColumns} FROM content_plugin_metadata WHERE id = @{nameof(id)}";

                ContentPluginMetadataEntity? entity = await dbSession.Connection.QuerySingleOrDefaultAsync<ContentPluginMetadataEntity>(
                    query, new { id }, transaction: dbSession.Transaction);

                return entity?.ToDomain();
            }, session);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<string>> GetUnreadAsync(string pluginId, int readerVersion, MediaTypeSet mediaTypes, string? after, int limit, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = PlainContents.UnreadSql(mediaTypes, $@"
                    EXISTS (
                        SELECT 1 FROM content_plugin_metadata
                        WHERE content_plugin_metadata.plugin_id = @{nameof(pluginId)}
                          AND content_plugin_metadata.sha256 = file.sha256
                          AND content_plugin_metadata.reader_version = @{nameof(readerVersion)})");

                IEnumerable<string> hashes = await dbSession.Connection.QueryAsync<string>(
                    query, new { pluginId, readerVersion, after, limit }, transaction: dbSession.Transaction);

                return (IReadOnlyList<string>)hashes.ToList();
            }, session);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<ContentPluginMetadata>> GetOfPluginAsync(string pluginId, string? afterSha256, int limit, IDbSession session)
        {
            // Before any there is, rather than a null the index could not start at.
            string after = afterSha256 ?? string.Empty;
            string query = $@"
                SELECT {SelectColumns} FROM content_plugin_metadata
                WHERE plugin_id = @{nameof(pluginId)} AND sha256 > @{nameof(after)}
                ORDER BY sha256
                LIMIT @{nameof(limit)}";

            IEnumerable<ContentPluginMetadataEntity> entities = await session.Connection.QueryAsync<ContentPluginMetadataEntity>(
                query, new { pluginId, after, limit }, transaction: session.Transaction);

            return entities.Select(entity => entity.ToDomain()).ToList();
        }

        /// <inheritdoc />
        public async Task<int> DeleteOfPluginAsync(string pluginId, DateTimeOffset at, IDbSession session)
        {
            string query = $"DELETE FROM content_plugin_metadata WHERE plugin_id = @{nameof(pluginId)} RETURNING id";
            return await _changeLog.DeleteAndLogAsync(ChangeEntityType.ContentPluginMetadata, query, new { pluginId }, at, session);
        }

        /// <inheritdoc />
        public async Task<bool> RecordAsync(ContentPluginMetadata metadata, IDbSession session)
        {
            bool isNew = await GetAsync(metadata.PluginId, metadata.Sha256, session) == null;
            int written = await session.Connection.ExecuteAsync(RecordSql, Parameters(metadata), transaction: session.Transaction);
            if (written == 0)
            {
                return false;
            }

            await WrittenAsync(metadata, isNew ? ChangeAction.Added : ChangeAction.Updated, metadata.ReadAt, session);
            return true;
        }

        /// <inheritdoc />
        public async Task<int> DeleteUnheldAsync(DateTimeOffset at, IDbSession session)
        {
            return await _changeLog.DeleteAndLogAsync(ChangeEntityType.ContentPluginMetadata, PlainContents.DeleteUnheldSql("content_plugin_metadata"), new { }, at, session);
        }

        /// <inheritdoc />
        public async Task DeleteUnheldByOthersAsync(string sha256, Guid fileId, DateTimeOffset at, IDbSession session)
        {
            await _changeLog.DeleteAndLogAsync(ChangeEntityType.ContentPluginMetadata, PlainContents.DeleteUnheldByOthersSql("content_plugin_metadata"), new { sha256, fileId }, at, session);
        }

        /// <inheritdoc />
        public async Task UpsertReplicatedAsync(ContentPluginMetadata metadata, string action, DateTimeOffset at, IDbSession session)
        {
            await session.Connection.ExecuteAsync(UpsertReplicatedSql, Parameters(metadata), transaction: session.Transaction);
            await WrittenAsync(metadata, action, at, session);
        }

        /// <inheritdoc />
        public async Task<bool> DeleteByIdAsync(Guid id, DateTimeOffset at, IDbSession session)
        {
            string query = $"DELETE FROM content_plugin_metadata WHERE id = @{nameof(id)} RETURNING id";
            return await _changeLog.DeleteAndLogAsync(ChangeEntityType.ContentPluginMetadata, query, new { id }, at, session) > 0;
        }

        /// <summary>What every write of a row is followed by: its fields' values are taken, and it is logged.</summary>
        private async Task WrittenAsync(ContentPluginMetadata metadata, string action, DateTimeOffset at, IDbSession session)
        {
            await _fieldValues.TakeAsync(metadata, session);
            await _changeLog.AppendAsync(ChangeEntityType.ContentPluginMetadata, metadata.Id, action, metadata, at, session);
        }

        private static object Parameters(ContentPluginMetadata row)
        {
            return new { row.Id, row.PluginId, row.Sha256, row.ReaderVersion, row.ReadAt, Document = row.Document?.Json };
        }

        internal sealed class ContentPluginMetadataEntity
        {
            public Guid Id { get; set; }
            public string PluginId { get; set; } = string.Empty;
            public string Sha256 { get; set; } = string.Empty;
            public int ReaderVersion { get; set; }
            public DateTimeOffset ReadAt { get; set; }
            public string? Document { get; set; }

            public ContentPluginMetadata ToDomain()
            {
                return new ContentPluginMetadata
                {
                    Id = Id,
                    PluginId = PluginId,
                    Sha256 = Sha256,
                    ReaderVersion = ReaderVersion,
                    ReadAt = ReadAt,
                    Document = Document == null ? null : PluginDocument.Parse(Document),
                };
            }
        }
    }
}
