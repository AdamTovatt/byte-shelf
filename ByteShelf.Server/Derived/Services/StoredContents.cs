using System.Threading.Channels;

namespace ByteShelf.Server.Derived.Services
{
    /// <summary>
    /// Contents just stored on this server, by an upload or a replica's sync, for what is derived from files to
    /// be made of them before what is missing from files stored long ago. Not kept over a restart: those are then
    /// missing like any others.
    /// </summary>
    public sealed class StoredContents
    {
        private readonly IReadOnlyDictionary<string, Channel<string>> _byPlugin;

        public StoredContents(IDerivedFilePlugins plugins)
        {
            _byPlugin = plugins.All.ToDictionary(plugin => plugin.Id, _ => Channel.CreateUnbounded<string>(), StringComparer.Ordinal);
        }

        /// <summary>Tells every plugin that the contents were stored, whatever their types.</summary>
        public void Add(string sha256)
        {
            foreach (Channel<string> channel in _byPlugin.Values)
            {
                channel.Writer.TryWrite(sha256);
            }
        }

        /// <summary>The contents stored since, as the plugin's maker comes to them.</summary>
        internal IAsyncEnumerable<string> ReadAllAsync(string pluginId, CancellationToken cancellationToken)
        {
            return _byPlugin[pluginId].Reader.ReadAllAsync(cancellationToken);
        }
    }
}
