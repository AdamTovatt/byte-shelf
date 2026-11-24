using ByteShelf.Server.Derived.Services;
using ByteShelf.Server.Plugins.Processes;

namespace ByteShelf.Server.Plugins.Services
{
    /// <summary>The plugins in the catalog that make derived files.</summary>
    internal sealed class LoadedDerivedFilePlugins : IDerivedFilePlugins
    {
        public LoadedDerivedFilePlugins(IPluginCatalog catalog)
        {
            All = catalog.Loaded
                .Where(plugin => plugin.Maker != null)
                .Select(plugin => new DerivedFilePlugin(new PluginTarget(plugin.Manifest.Id, plugin.Folder), plugin.Maker!.Makes))
                .ToList();
        }

        public IReadOnlyList<DerivedFilePlugin> All { get; }
    }
}
