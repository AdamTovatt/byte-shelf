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

        public FilePluginMetadataRepository(DbDataSource dataSource, IDbSessionFactory sessionFactory, IChangeLogRepository changeLog)
            : base(dataSource, sessionFactory)
        {
            _changeLog = changeLog;
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

            string action = expectedVersion == null ? ChangeAction.Added : ChangeAction.Updated;
            await _changeLog.AppendAsync(ChangeEntityType.FilePluginMetadata, metadata.Id, action, metadata, at, session);
            return metadata;
        }

        /// <inheritdoc />
        public async Task CopyAsync(Guid fromFileId, Guid toFileId, DateTimeOffset at, IDbSession session)
        {
            foreach (FilePluginMetadata source in await GetOfFileAsync(fromFileId, session))
            {
                FilePluginMetadata copy = FilePluginMetadata.Of(source.PluginId, toFileId, source.Version, source.Document);
                await session.Connection.ExecuteAsync(InsertSql, Parameters(copy), transaction: session.Transaction);
                await _changeLog.AppendAsync(ChangeEntityType.FilePluginMetadata, copy.Id, ChangeAction.Added, copy, at, session);
            }
        }

        /// <inheritdoc />
        public async Task LogAgainAsync(Guid fileId, DateTimeOffset at, IDbSession session)
        {
            foreach (FilePluginMetadata metadata in await GetOfFileAsync(fileId, session))
            {
                await _changeLog.AppendAsync(ChangeEntityType.FilePluginMetadata, metadata.Id, ChangeAction.Updated, metadata, at, session);
            }
        }

        /// <inheritdoc />
        public async Task UpsertReplicatedAsync(FilePluginMetadata metadata, string action, DateTimeOffset at, IDbSession session)
        {
            await session.Connection.ExecuteAsync(UpsertReplicatedSql, Parameters(metadata), transaction: session.Transaction);
            await _changeLog.AppendAsync(ChangeEntityType.FilePluginMetadata, metadata.Id, action, metadata, at, session);
        }

        /// <inheritdoc />
        public async Task DeleteWithoutFileAsync(DateTimeOffset at, IDbSession session)
        {
            string query = @"
                DELETE FROM file_plugin_metadata
                WHERE NOT EXISTS (SELECT 1 FROM file WHERE file.id = file_plugin_metadata.file_id)
                RETURNING id";

            await _changeLog.DeleteAndLogAsync(ChangeEntityType.FilePluginMetadata, query, new { }, at, session);
        }

        /// <inheritdoc />
        public async Task<bool> DeleteByIdAsync(Guid id, DateTimeOffset at, IDbSession session)
        {
            string query = $"DELETE FROM file_plugin_metadata WHERE id = @{nameof(id)} RETURNING id";
            return await _changeLog.DeleteAndLogAsync(ChangeEntityType.FilePluginMetadata, query, new { id }, at, session) > 0;
        }

        private static async Task<IReadOnlyList<FilePluginMetadata>> GetOfFileAsync(Guid fileId, IDbSession session)
        {
            string query = $"SELECT {SelectColumns} FROM file_plugin_metadata WHERE file_id = @{nameof(fileId)} ORDER BY plugin_id";

            IEnumerable<FilePluginMetadataEntity> entities = await session.Connection.QueryAsync<FilePluginMetadataEntity>(
                query, new { fileId }, transaction: session.Transaction);

            return entities.Select(entity => entity.ToDomain()).ToList();
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
