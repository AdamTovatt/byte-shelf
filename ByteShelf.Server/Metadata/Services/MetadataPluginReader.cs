using System.Text.Json.Nodes;
using ByteShelf.Server.Metadata.Models;

namespace ByteShelf.Server.Metadata.Services
{
    /// <summary>A plugin's reader with what it says of itself, asked once and checked when the plugin is taken up.</summary>
    public sealed class MetadataPluginReader
    {
        private readonly IContentDocumentReader _reader;

        /// <exception cref="ArgumentException">One of the types the reader takes is no media type or family of them.</exception>
        public MetadataPluginReader(IContentDocumentReader reader)
        {
            _reader = reader;
            Version = reader.Version;
            MediaTypes = MediaTypeSet.Of(reader.MediaTypes);
        }

        public int Version { get; }

        public MediaTypeSet MediaTypes { get; }

        public Task<JsonObject?> ReadAsync(Stream contents, CancellationToken cancellationToken)
        {
            return _reader.ReadAsync(contents, cancellationToken);
        }
    }
}
