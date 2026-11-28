using System.Threading.Channels;
using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Storage.Services;

namespace ByteShelf.Server.Derived.Services
{
    /// <summary>
    /// Contents just stored on this server, by an upload or a replica's sync, for what is derived from files to
    /// be made of them before what is missing from files stored long ago. Not kept over a restart: those are then
    /// missing like any others.
    /// </summary>
    public sealed class StoredContents : IStoredContentsListener
    {
        private readonly IReadOnlyDictionary<(string PluginId, MakingGroup Group), Channel<string>> _byMaker;

        public StoredContents(IDerivedFilePlugins plugins)
        {
            _byMaker = MakersOf(plugins).ToDictionary(maker => (maker.Plugin.Id, maker.Group), _ => Channel.CreateUnbounded<string>());
        }

        /// <summary>Tells every plugin, of each group it makes in the background, that the contents were stored, whatever their types.</summary>
        public void Stored(string sha256)
        {
            foreach (Channel<string> channel in _byMaker.Values)
            {
                channel.Writer.TryWrite(sha256);
            }
        }

        /// <summary>The plugins with each group they make in the background (<see cref="MakingGroup.InBackground"/>): each goes at its own pace.</summary>
        internal static IEnumerable<(DerivedFilePlugin Plugin, MakingGroup Group)> MakersOf(IDerivedFilePlugins plugins)
        {
            return plugins.All.SelectMany(plugin => MakingGroup.InBackground.Where(plugin.MakesAny).Select(group => (plugin, group)));
        }

        /// <summary>The contents stored since, as the plugin's maker of <paramref name="group"/> comes to them.</summary>
        internal IAsyncEnumerable<string> ReadAllAsync(string pluginId, MakingGroup group, CancellationToken cancellationToken)
        {
            return _byMaker[(pluginId, group)].Reader.ReadAllAsync(cancellationToken);
        }
    }
}
