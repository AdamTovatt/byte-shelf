using System.Runtime.ExceptionServices;
using ByteShelf.Plugins;
using ByteShelf.Server.Metadata.Services;
using ByteShelf.Server.Plugins.Models;

namespace ByteShelf.Server.Plugins.Services
{
    /// <summary>The readers of the plugins in the catalog, each made once and run in this process.</summary>
    internal sealed class LoadedPluginReaders : IPluginReaders
    {
        public LoadedPluginReaders(IPluginCatalog catalog)
        {
            All = catalog.Loaded
                .Where(plugin => plugin.Reader != null)
                .Select(plugin => ReaderOf(plugin.Manifest.Id, plugin.Reader!))
                .ToList();
        }

        public IReadOnlyList<PluginReader> All { get; }

        private static PluginReader ReaderOf(string pluginId, PluginReaderRegistration registered)
        {
            // Off the caller's thread: its constructor is the plugin's code, and may block.
            Lazy<Task<IContentDocumentReader>> made = new Lazy<Task<IContentDocumentReader>>(() => Task.Run(() => Make(registered)));
            return new PluginReader(pluginId, () => made.Value, registered.Version, registered.MediaTypes);
        }

        private static IContentDocumentReader Make(PluginReaderRegistration registered)
        {
            try
            {
                return registered.Make();
            }
            catch (Exception exception)
            {
                ExceptionDispatchInfo.Throw(PluginFailure.CauseOf(exception));
                throw;
            }
        }
    }
}
