using System.Text.Json.Nodes;
using ByteShelf.Plugins;

namespace ByteShelf.Server.Tests.Plugin
{
    /// <summary>A reader that needs a service nobody registers.</summary>
    public sealed class NeedingReader : IContentDocumentReader
    {
        public NeedingReader(Uri missing)
        {
            ArgumentNullException.ThrowIfNull(missing);
        }

        public Task<JsonObject?> ReadAsync(Stream contents, CancellationToken cancellationToken)
        {
            return Task.FromResult<JsonObject?>(null);
        }
    }
}
