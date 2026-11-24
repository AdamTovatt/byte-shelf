using ByteShelf.Server.Plugins.Models;

namespace ByteShelf.Server.Plugins.Services
{
    public sealed class PluginCatalog : IPluginCatalog
    {
        /// <summary>A server without plugins.</summary>
        public static readonly PluginCatalog Empty = new PluginCatalog([], []);

        public PluginCatalog(IReadOnlyList<LoadedPlugin> loaded, IReadOnlyList<SkippedPlugin> skipped)
        {
            Loaded = loaded;
            Skipped = skipped;
        }

        public IReadOnlyList<LoadedPlugin> Loaded { get; }

        public IReadOnlyList<SkippedPlugin> Skipped { get; }
    }
}
