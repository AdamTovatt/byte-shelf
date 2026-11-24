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
    public class FilePluginMetadataRepository : RepositoryBase, IFilePluginMetadataRepository
    {
        private const string SelectColumns = $@"
            id,
            plugin_id AS {nameof(FilePluginMetadataEntity.PluginId)},
            file_id AS {nameof(FilePluginMetadataEntity.FileId)},
            version,
            document";

        private const string InsertSql = @"
            INSERT INTO file_plugin_metadata (id, plugin_id, file_id, version, document)
            VALUES (@Id, @PluginId, @FileId, @Version, @Document)";

        private static readonly string UpsertReplicatedSql = UpsertSql.OnIdConflict(InsertSql);

        private readonly IChangeLogRepository _changeLog;
        private readonly IPluginFieldValueRepository _fieldValues;

        public FilePluginMetadataRepository(DbDataSource dataSource, IDbSessionFactory sessionFactory, IChangeLogRepository changeLog, IPluginFieldValueRepository fieldValues)
            : base(dataSource, sessionFactory)
        {
            _changeLog = changeLog;
            _fieldValues = fieldValues;
        }

        /// <inheritdoc />
        public async Task<FilePluginMetadata?> GetAsync(string pluginId, Guid fileId, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                Guid id = FilePluginMetadata.IdFor(pluginId, fileId);
                string query = $"SELECT {SelectColumns} FROM file_plugin_metadata WHERE id = @{nameof(id)}";

                FilePluginMetadataEntity? entity = await dbSession.Connection.QuerySingleOrDefaultAsync<FilePluginMetadataEntity>(
                    query, new { id }, transaction: dbSession.Transaction);

                return entity?.ToDomain();
            }, session);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<FilePluginMetadata>> GetOfPluginAsync(string pluginId, Guid? afterFileId, int limit, IDbSession session)
        {
            // Before any there is, rather than a null the index could not start at.
            Guid after = afterFileId ?? Guid.Empty;
            string query = $@"
                SELECT {SelectColumns} FROM file_plugin_metadata
                WHERE plugin_id = @{nameof(pluginId)} AND file_id > @{nameof(after)}
                ORDER BY file_id
                LIMIT @{nameof(limit)}";

            IEnumerable<FilePluginMetadataEntity> entities = await session.Connection.QueryAsync<FilePluginMetadataEntity>(
                query, new { pluginId, after, limit }, transaction: session.Transaction);

            return entities.Select(entity => entity.ToDomain()).ToList();
        }

        /// <inheritdoc />
        public async Task<int> DeleteOfPluginAsync(string pluginId, DateTimeOffset at, IDbSession session)
        {
            string query = $"DELETE FROM file_plugin_metadata WHERE plugin_id = @{nameof(pluginId)} RETURNING id";
            return await _changeLog.DeleteAndLogAsync(ChangeEntityType.FilePluginMetadata, query, new { pluginId }, at, session);
        }

        /// <inheritdoc />
        public async Task<FilePluginMetadata?> SetAsync(string pluginId, Guid fileId, int? expectedVersion, PluginDocument? document, DateTimeOffset at, IDbSession session)
        {
            FilePluginMetadata metadata = FilePluginMetadata.Of(pluginId, fileId, (expectedVersion ?? 0) + 1, document);
            string? json = document?.Json;
            int written = expectedVersion == null
                ? await session.Connection.ExecuteAsync($"{InsertSql} ON CONFLICT (id) DO NOTHING", Parameters(metadata), transaction: session.Transaction)
                : await session.Connection.ExecuteAsync(
                    $"UPDATE file_plugin_metadata SET version = version + 1, document = @{nameof(json)} WHERE id = @{nameof(metadata.Id)} AND version = @{nameof(expectedVersion)}",
                    new { metadata.Id, json, expectedVersion },
                    transaction: session.Transaction);

            if (written == 0)
            {
                return null;
            }

            await WrittenAsync(metadata, expectedVersion == null ? ChangeAction.Added : ChangeAction.Updated, at, session);
            return metadata;
        }

        /// <inheritdoc />
        public async Task CopyAsync(Guid fromFileId, Guid toFileId, DateTimeOffset at, IDbSession session)
        {
            foreach (FilePluginMetadata source in await GetOfFilesAsync([fromFileId], session))
            {
                FilePluginMetadata copy = FilePluginMetadata.Of(source.PluginId, toFileId, source.Version, source.Document);
                await session.Connection.ExecuteAsync(InsertSql, Parameters(copy), transaction: session.Transaction);
                await WrittenAsync(copy, ChangeAction.Added, at, session);
            }
        }

        /// <inheritdoc />
        public async Task LogAgainAsync(IReadOnlyCollection<Guid> fileIds, DateTimeOffset at, IDbSession session)
        {
            foreach (FilePluginMetadata metadata in await GetOfFilesAsync(fileIds, session))
            {
                await _changeLog.AppendAsync(ChangeEntityType.FilePluginMetadata, metadata.Id, ChangeAction.Updated, metadata, at, session);
            }
        }

        /// <inheritdoc />
        public async Task UpsertReplicatedAsync(FilePluginMetadata metadata, string action, DateTimeOffset at, IDbSession session)
        {
            await session.Connection.ExecuteAsync(UpsertReplicatedSql, Parameters(metadata), transaction: session.Transaction);
            await WrittenAsync(metadata, action, at, session);
        }

        /// <inheritdoc />
        public async Task<bool> DeleteByIdAsync(Guid id, DateTimeOffset at, IDbSession session)
        {
            string query = $"DELETE FROM file_plugin_metadata WHERE id = @{nameof(id)} RETURNING id";
            return await _changeLog.DeleteAndLogAsync(ChangeEntityType.FilePluginMetadata, query, new { id }, at, session) > 0;
        }

        private static async Task<IReadOnlyList<FilePluginMetadata>> GetOfFilesAsync(IReadOnlyCollection<Guid> fileIds, IDbSession session)
        {
            string fileIdsJson = GuidTypeHandler.ToJsonArray(fileIds);
            string query = $@"
                SELECT {SelectColumns} FROM file_plugin_metadata
                WHERE file_id IN (SELECT value FROM json_each(@{nameof(fileIdsJson)}))
                ORDER BY file_id, plugin_id";

            IEnumerable<FilePluginMetadataEntity> entities = await session.Connection.QueryAsync<FilePluginMetadataEntity>(
                query, new { fileIdsJson }, transaction: session.Transaction);

            return entities.Select(entity => entity.ToDomain()).ToList();
        }

        /// <summary>What every write of a row is followed by: its fields' values are taken, and it is logged.</summary>
        private async Task WrittenAsync(FilePluginMetadata metadata, string action, DateTimeOffset at, IDbSession session)
        {
            await _fieldValues.TakeAsync(metadata, session);
            await _changeLog.AppendAsync(ChangeEntityType.FilePluginMetadata, metadata.Id, action, metadata, at, session);
        }

        private static object Parameters(FilePluginMetadata row)
        {
            return new { row.Id, row.PluginId, row.FileId, row.Version, Document = row.Document?.Json };
        }

        internal sealed class FilePluginMetadataEntity
        {
            public Guid Id { get; set; }
            public string PluginId { get; set; } = string.Empty;
            public Guid FileId { get; set; }
            public int Version { get; set; }
            public string? Document { get; set; }

            public FilePluginMetadata ToDomain()
            {
                return new FilePluginMetadata
                {
                    Id = Id,
                    PluginId = PluginId,
                    FileId = FileId,
                    Version = Version,
                    Document = Document == null ? null : PluginDocument.Parse(Document),
                };
            }
        }
    }
}
