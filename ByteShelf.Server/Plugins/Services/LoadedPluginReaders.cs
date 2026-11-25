using ByteShelf.Server.Metadata.Services;
using ByteShelf.Server.Plugins.Models;
using ByteShelf.Server.Plugins.Processes;

namespace ByteShelf.Server.Plugins.Services
{
    /// <summary>The readers of the plugins in the catalog, each run in its plugin's process.</summary>
    internal sealed class LoadedPluginReaders : IPluginReaders
    {
        private readonly PluginProcesses _processes;

        public LoadedPluginReaders(IPluginCatalog catalog, PluginProcesses processes)
        {
            _processes = processes;
            All = catalog.Loaded
                .Where(plugin => plugin.Reader != null)
                .Select(plugin => ReaderOf(plugin, plugin.Reader!))
                .ToList();
        }

        public IReadOnlyList<PluginReader> All { get; }

        private PluginReader ReaderOf(LoadedPlugin plugin, PluginReaderRegistration registered)
        {
            PluginTarget target = new PluginTarget(plugin.Manifest.Id, plugin.Folder);
            return new PluginReader(target.Id, registered.Version, registered.MediaTypes, async (sha256, cancellationToken) =>
            {
                using PluginTurns.Turn turn = _processes.Enter(target.Id, PluginPriority.Backlog);
                return await _processes.RunAsync(new ReadRun(target, sha256, registered.Version), turn, cancellationToken);
            });
        }
    }
}
