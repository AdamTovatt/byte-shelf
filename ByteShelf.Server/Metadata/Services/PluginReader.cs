using ByteShelf.Plugins;
using ByteShelf.Server.Metadata.Models;

namespace ByteShelf.Server.Metadata.Services
{
    /// <summary>A plugin's reader of files' contents.</summary>
    /// <param name="PluginId">The plugin's id.</param>
    /// <param name="GetAsync">What reads them, however it is run: made the first time it is asked for, which may fail or never end, and the same one after.</param>
    /// <param name="Version">Its version: what another version read is read again.</param>
    /// <param name="MediaTypes">The types it takes.</param>
    public sealed record PluginReader(string PluginId, Func<Task<IContentDocumentReader>> GetAsync, int Version, MediaTypeSet MediaTypes);
}
