using ByteShelf.Server.Metadata.Models;

namespace ByteShelf.Server.Plugins.Models
{
    /// <summary>What a plugin registered to read files' contents with.</summary>
    /// <param name="Service">The service that is the reader (<see cref="ByteShelf.Plugins.IContentDocumentReader"/>).</param>
    /// <param name="Version">The reader's version: what another version read is read again.</param>
    /// <param name="MediaTypes">The types it takes.</param>
    public sealed record PluginReaderRegistration(Type Service, int Version, MediaTypeSet MediaTypes);
}
