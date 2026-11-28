using ByteShelf.Server.Plugins.Processes;
using ByteShelf.Server.Storage.Models;

namespace ByteShelf.Server.Metadata.Services
{
    /// <summary>A plugin's reader of files' contents.</summary>
    /// <param name="PluginId">The plugin's id.</param>
    /// <param name="Version">Its version: what another version read is read again.</param>
    /// <param name="MediaTypes">The types it takes.</param>
    /// <param name="Read">Reads contents, however it is run.</param>
    public sealed record PluginReader(string PluginId, int Version, MediaTypeSet MediaTypes, ReadContents Read)
    {
        /// <summary>The lane every reading takes: it is of a minute at most, as what is read is waited for by listings.</summary>
        public static PluginLane Lane { get; } = PluginLane.Quick;
    }

    /// <summary>Has a plugin's reader read the contents <paramref name="sha256"/>, unless no plain file holds them.</summary>
    /// <exception cref="Plugins.Processes.PluginUnavailableException">The reader could not be run, which says nothing of the contents.</exception>
    /// <exception cref="OperationCanceledException">It was stopped, which says nothing of the contents.</exception>
    public delegate Task<PluginReading> ReadContents(string sha256, CancellationToken cancellationToken);
}
