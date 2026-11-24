using ByteShelf.Server.Derived.Models;
using ByteShelfCommon;

namespace ByteShelf.Server.Derived.Services
{
    /// <summary>Which plugin makes what, and what each has yet to.</summary>
    internal sealed class DerivedMakers
    {
        private readonly IDerivedFilePlugins _plugins;
        private readonly DerivedStore _store;

        public DerivedMakers(IDerivedFilePlugins plugins, DerivedStore store)
        {
            _plugins = plugins;
            _store = store;
        }

        /// <summary>
        /// What makes <paramref name="kind"/> from contents that files of <paramref name="mediaTypes"/> hold; null
        /// when nothing does. One plugin whatever file is asked about, since identical contents have one derived
        /// file of a kind: one that takes one of the types exactly before one that takes its family, then the
        /// first by id.
        /// </summary>
        public KindMaker? For(DerivedKind kind, IReadOnlyCollection<string> mediaTypes)
        {
            return _plugins.All
                .SelectMany(plugin => plugin.Makes.Select(declaration => new KindMaker(plugin, declaration)))
                .Where(maker => maker.Declaration.Kind == kind && mediaTypes.Any(maker.Declaration.MediaTypes.Includes))
                .OrderBy(maker => mediaTypes.Any(maker.Declaration.MediaTypes.Names) ? 0 : 1)
                .ThenBy(maker => maker.Plugin.Id, StringComparer.Ordinal)
                .FirstOrDefault();
        }

        /// <summary>
        /// The kinds of <paramref name="group"/> that <paramref name="plugin"/> is what makes from contents files of
        /// <paramref name="mediaTypes"/> hold, and that it has yet to.
        /// </summary>
        public IReadOnlyList<KindMaker> MissingOf(DerivedFilePlugin plugin, MakingGroup group, string sha256, IReadOnlyCollection<string> mediaTypes)
        {
            return DerivedKinds.In(group)
                .Select(kind => For(kind, mediaTypes))
                .OfType<KindMaker>()
                .Where(maker => maker.Plugin.Id == plugin.Id && !maker.IsCurrent(_store.Find(maker.Declaration.Kind, sha256)))
                .ToList();
        }

        /// <summary>Whether an installed plugin with the id <paramref name="pluginId"/> makes <paramref name="kind"/>.</summary>
        public bool Makes(DerivedKind kind, string pluginId)
        {
            return _plugins.All.Any(plugin => plugin.Id == pluginId && plugin.Makes.Any(declaration => declaration.Kind == kind));
        }
    }
}
