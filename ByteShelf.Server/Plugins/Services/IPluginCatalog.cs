using ByteShelf.Server.Plugins.Models;

namespace ByteShelf.Server.Plugins.Services
{
    /// <summary>The plugins found when the server started. They do not change while it runs.</summary>
    public interface IPluginCatalog
    {
        /// <summary>The plugins loaded, by id.</summary>
        IReadOnlyList<LoadedPlugin> Loaded { get; }

        /// <summary>The folders whose plugin was not loaded, by folder.</summary>
        IReadOnlyList<SkippedPlugin> Skipped { get; }
    }
}
