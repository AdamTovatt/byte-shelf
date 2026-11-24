using System.Text.Json.Nodes;
using ByteShelf.Server.Metadata.Models;
using ByteShelf.Server.Metadata.Repositories;
using ByteShelfCommon;

namespace ByteShelf.Server.Metadata.Services
{
    /// <summary>A plugin's reader, whose findings go to <c>content_plugin_metadata</c>.</summary>
    internal sealed class PluginContentReader : ContentReader
    {
        private readonly string _pluginId;
        private readonly MetadataPluginReader _reader;
        private readonly IContentPluginMetadataRepository _repository;

        public PluginContentReader(string pluginId, MetadataPluginReader reader, IContentPluginMetadataRepository repository)
        {
            _pluginId = pluginId;
            _reader = reader;
            _repository = repository;
        }

        // A plugin has one reader at most.
        public override string Id => _pluginId;

        public override RecordReading Nothing => Record(null);

        public override Task<IReadOnlyList<string>> GetUnreadAsync(string? after, int limit)
        {
            return _repository.GetUnreadAsync(_pluginId, _reader.Version, _reader.MediaTypes, after, limit);
        }

        public override async Task<RecordReading> ReadAsync(Stream contents, CancellationToken cancellationToken)
        {
            JsonObject? found = await _reader.ReadAsync(contents, cancellationToken);
            return Record(found == null ? null : PluginDocument.Parse(found.ToJsonString()));
        }

        private RecordReading Record(PluginDocument? document)
        {
            return (sha256, at, session) => _repository.RecordAsync(ContentPluginMetadata.Of(_pluginId, sha256, _reader.Version, at, document), session);
        }
    }
}
