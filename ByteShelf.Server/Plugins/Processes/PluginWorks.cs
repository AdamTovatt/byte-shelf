namespace ByteShelf.Server.Plugins.Processes
{
    /// <summary>What one plugin does in its process, by what a run request calls each work (<see cref="ProcessWork.NameOf"/>).</summary>
    internal sealed class PluginWorks
    {
        private readonly IReadOnlyDictionary<string, PluginWork> _byName;

        private PluginWorks(IReadOnlyDictionary<string, PluginWork> byName)
        {
            _byName = byName;
        }

        /// <summary>How the plugin that registered <paramref name="registered"/> does each of <paramref name="works"/>.</summary>
        public static PluginWorks Of(IEnumerable<ProcessWork> works, Models.PluginRegistrations registered)
        {
            return new PluginWorks(works.ToDictionary(work => ProcessWork.NameOf(work.GetType()), work => work.For(registered), StringComparer.Ordinal));
        }

        /// <exception cref="PluginUnavailableException">It does no work of that name.</exception>
        public PluginWork Named(string name)
        {
            return _byName.TryGetValue(name, out PluginWork? work) ? work : throw new PluginUnavailableException($"it does not do '{name}'");
        }
    }
}
