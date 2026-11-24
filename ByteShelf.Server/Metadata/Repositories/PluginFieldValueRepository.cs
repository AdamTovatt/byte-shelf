using System.Data.Common;
using System.Text.Json;
using ByteShelf.Server.Metadata.Models;
using ByteShelf.Server.Metadata.Services;
using ByteShelf.Server.Shared.Database;
using ByteShelf.Server.Storage.Models;
using ByteShelfCommon;
using ByteShelfCommon.Serialization;
using EasyReasy.Database;
using EasyReasy.Database.Mapping;

namespace ByteShelf.Server.Metadata.Repositories
{
    public class PluginFieldValueRepository : RepositoryBase, IPluginFieldValueRepository
    {
        private readonly IMetadataPlugins _plugins;

        public PluginFieldValueRepository(DbDataSource dataSource, IDbSessionFactory sessionFactory, IMetadataPlugins plugins)
            : base(dataSource, sessionFactory)
        {
            _plugins = plugins;
        }

        /// <inheritdoc />
        public Task TakeAsync(ContentPluginMetadata row, IDbSession session)
        {
            return TakeAsync(row.PluginId, Subject.Contents(row.Sha256), row.Document, session);
        }

        /// <inheritdoc />
        public Task TakeAsync(FilePluginMetadata row, IDbSession session)
        {
            return TakeAsync(row.PluginId, Subject.File(row.FileId), row.Document, session);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyDictionary<Guid, ListedFields>> GetListedAsync(
            IReadOnlyCollection<FileRecord> files, IDbSession? session = null)
        {
            if (files.Count == 0 || _plugins.All.Count == 0)
            {
                return new Dictionary<Guid, ListedFields>();
            }

            string idsJson = GuidTypeHandler.ToJsonArray(files.Select(file => file.Id));
            string query = $@"
                SELECT file.id AS {nameof(ListedEntity.FileId)}, value.plugin_id AS {nameof(ListedEntity.PluginId)}, value.field, value.listed
                FROM json_each(@{nameof(idsJson)}) AS id
                JOIN file ON file.id = id.value
                JOIN plugin_field_value AS value ON {PluginFieldValues.OfFile("value")}
                WHERE value.listed IS NOT NULL";

            IEnumerable<ListedEntity> entities = await UseSessionAsync(
                (dbSession) => dbSession.Connection.QueryAsync<ListedEntity>(query, new { idsJson }, transaction: dbSession.Transaction), session);

            return entities
                .GroupBy(entity => entity.FileId)
                .ToDictionary(
                    ofFile => ofFile.Key,
                    ofFile => (ListedFields)ofFile
                        .GroupBy(entity => entity.PluginId)
                        .ToDictionary(
                            ofPlugin => ofPlugin.Key,
                            ofPlugin => (IReadOnlyDictionary<string, PluginFieldValue>)ofPlugin.ToDictionary(entity => entity.Field, entity => entity.ToDomain())));
        }

        /// <inheritdoc />
        public async Task<IReadOnlyDictionary<string, string>> GetDeclaredFieldsAsync(IDbSession session)
        {
            IEnumerable<DeclaredEntity> entities = await session.Connection.QueryAsync<DeclaredEntity>(
                $"SELECT plugin_id AS {nameof(DeclaredEntity.PluginId)}, fields FROM plugin_declared_fields", transaction: session.Transaction);

            return entities.ToDictionary(entity => entity.PluginId, entity => entity.Fields, StringComparer.Ordinal);
        }

        /// <inheritdoc />
        public async Task SetDeclaredFieldsAsync(string pluginId, string fields, IDbSession session)
        {
            string query = $@"
                INSERT INTO plugin_declared_fields (plugin_id, fields) VALUES (@{nameof(pluginId)}, @{nameof(fields)})
                ON CONFLICT (plugin_id) DO UPDATE SET fields = excluded.fields";

            await session.Connection.ExecuteAsync(query, new { pluginId, fields }, transaction: session.Transaction);
        }

        /// <inheritdoc />
        public async Task DropAsync(string pluginId, IDbSession session)
        {
            await session.Connection.ExecuteAsync(
                $"DELETE FROM plugin_field_value WHERE plugin_id = @{nameof(pluginId)}", new { pluginId }, transaction: session.Transaction);
            await session.Connection.ExecuteAsync(
                $"DELETE FROM plugin_declared_fields WHERE plugin_id = @{nameof(pluginId)}", new { pluginId }, transaction: session.Transaction);
        }

        private async Task TakeAsync(string pluginId, Subject subject, PluginDocument? document, IDbSession session)
        {
            await session.Connection.ExecuteAsync(
                $"DELETE FROM plugin_field_value WHERE plugin_id = @{nameof(pluginId)} AND {subject.Condition}",
                new { pluginId, subject.Sha256, subject.FileId },
                transaction: session.Transaction);

            if (document == null || _plugins.Installed.Find(pluginId) is not { } plugin)
            {
                return;
            }

            JsonElement read = document.ToElement();

            string insertQuery = $@"
                INSERT INTO plugin_field_value (plugin_id, field, sha256, file_id, text_fold, number, instant, flag, searched, listed)
                VALUES (@{nameof(pluginId)}, @Field, @{nameof(subject.Sha256)}, @{nameof(subject.FileId)}, @TextFold, @Number, @Instant, @Flag, @Searched, @Listed)";

            foreach (PluginField field in plugin.Fields.Where(field => field.Document == subject.Document))
            {
                if (PluginFieldValue.Of(field, read) is not { } value)
                {
                    continue;
                }

                await session.Connection.ExecuteAsync(
                    insertQuery,
                    new
                    {
                        pluginId,
                        Field = field.Name,
                        subject.Sha256,
                        subject.FileId,
                        TextFold = value.FoldedText(),
                        value.Number,
                        Instant = value.Date,
                        value.Flag,
                        field.Searched,
                        Listed = field.Listed && value.FitsListings() ? JsonSerializer.Serialize(value, ByteShelfJsonContext.Default.PluginFieldValue) : null,
                    },
                    transaction: session.Transaction);
            }
        }

        /// <summary>What a document is about, as a row names it.</summary>
        private sealed record Subject
        {
            private Subject()
            {
            }

            public required PluginDocumentSubject Document { get; init; }

            public string? Sha256 { get; private init; }

            public Guid? FileId { get; private init; }

            /// <summary>The condition a row about it meets, on the one column an index of it starts with.</summary>
            public required string Condition { get; init; }

            public static Subject Contents(string sha256)
            {
                return new Subject { Document = PluginDocumentSubject.Contents, Sha256 = sha256, Condition = $"sha256 = @{nameof(Sha256)}" };
            }

            public static Subject File(Guid fileId)
            {
                return new Subject { Document = PluginDocumentSubject.File, FileId = fileId, Condition = $"file_id = @{nameof(FileId)}" };
            }
        }

        internal sealed class ListedEntity
        {
            public Guid FileId { get; set; }
            public string PluginId { get; set; } = string.Empty;
            public string Field { get; set; } = string.Empty;
            public string Listed { get; set; } = string.Empty;

            public PluginFieldValue ToDomain()
            {
                return JsonSerializer.Deserialize(Listed, ByteShelfJsonContext.Default.PluginFieldValue)!;
            }
        }

        internal sealed class DeclaredEntity
        {
            public string PluginId { get; set; } = string.Empty;
            public string Fields { get; set; } = string.Empty;
        }
    }
}
