using ByteShelf.Plugins;
using ByteShelf.Server.Metadata.Services;
using ByteShelf.Server.Plugins.Models;

namespace ByteShelf.Server.Plugins.Services
{
    /// <summary>The plugins the server loaded, as far as metadata goes. Their readers are made with it.</summary>
    internal sealed class LoadedMetadataPlugins : IMetadataPlugins
    {
        public LoadedMetadataPlugins(IPluginCatalog catalog, IServiceProvider services, ILogger<LoadedMetadataPlugins> logger)
        {
            All = catalog.Loaded
                .Select(plugin => new MetadataPlugin(plugin.Manifest.Declaration, plugin.Reader == null ? null : Make(plugin.Reader, plugin.Manifest.Id, services, logger)))
                .ToList();
        }

        public IReadOnlyList<MetadataPlugin> All { get; }

        /// <summary>Makes the reader; null when that fails, so it is not asked and nothing is recorded as read by it.</summary>
        private static MetadataPluginReader? Make(PluginReaderRegistration reader, string pluginId, IServiceProvider services, ILogger logger)
        {
            try
            {
                return new MetadataPluginReader((IContentDocumentReader)services.GetRequiredService(reader.Service), reader.Version, reader.MediaTypes);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "The reader of the plugin {Plugin} could not be made, so it reads nothing until the server restarts.", pluginId);
                return null;
            }
        }
    }
}
