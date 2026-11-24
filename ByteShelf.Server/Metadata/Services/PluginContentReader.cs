using System.Text.Json.Nodes;
using ByteShelf.Plugins;
using ByteShelf.Server.Metadata.Models;
using ByteShelf.Server.Metadata.Repositories;
using ByteShelfCommon;

namespace ByteShelf.Server.Metadata.Services
{
    /// <summary>A plugin's reader, whose findings go to <c>content_plugin_metadata</c>.</summary>
    internal sealed class PluginContentReader : ContentReader
    {
        private readonly PluginReader _reader;
        private readonly IContentPluginMetadataRepository _repository;

        public PluginContentReader(PluginReader reader, IContentPluginMetadataRepository repository)
        {
            _reader = reader;
            _repository = repository;
        }

        // A plugin has one reader at most.
        public override string Id => _reader.PluginId;

        public override RecordReading Nothing => Record(null);

        public override Task PrepareAsync()
        {
            return _reader.GetAsync();
        }

        public override Task<IReadOnlyList<string>> GetUnreadAsync(string? after, int limit)
        {
            return _repository.GetUnreadAsync(_reader.PluginId, _reader.Version, _reader.MediaTypes, after, limit);
        }

        public override async Task<RecordReading> ReadAsync(Stream contents, CancellationToken cancellationToken)
        {
            IContentDocumentReader reader = await _reader.GetAsync();
            JsonObject? found = await reader.ReadAsync(contents, cancellationToken);
            return Record(found == null ? null : PluginDocument.Parse(found.ToJsonString()));
        }

        private RecordReading Record(PluginDocument? document)
        {
            return (sha256, at, session) => _repository.RecordAsync(ContentPluginMetadata.Of(_reader.PluginId, sha256, _reader.Version, at, document), session);
        }
    }
}
