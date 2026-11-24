using ByteShelf.Plugins;
using ByteShelf.Server.Metadata.Models;

namespace ByteShelf.Server.Metadata.Services
{
    /// <summary>A plugin's reader of files' contents.</summary>
    /// <param name="DocumentReader">What reads them.</param>
    /// <param name="Version">Its version: what another version read is read again.</param>
    /// <param name="MediaTypes">The types it takes.</param>
    public sealed record MetadataPluginReader(IContentDocumentReader DocumentReader, int Version, MediaTypeSet MediaTypes);
}
