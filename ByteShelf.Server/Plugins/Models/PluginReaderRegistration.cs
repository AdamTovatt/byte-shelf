using ByteShelf.Plugins;
using ByteShelf.Server.Storage.Models;

namespace ByteShelf.Server.Plugins.Models
{
    /// <summary>What a plugin registered to read files' contents with.</summary>
    /// <param name="Make">Makes the reader, running its constructor: in a child process alone.</param>
    /// <param name="Version">The reader's version: what another version read is read again.</param>
    /// <param name="MediaTypes">The types it takes.</param>
    public sealed record PluginReaderRegistration(Func<IContentDocumentReader> Make, int Version, MediaTypeSet MediaTypes);
}
