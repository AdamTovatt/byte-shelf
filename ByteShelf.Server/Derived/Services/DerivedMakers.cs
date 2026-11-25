using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Storage.Models;
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
        /// Whether <paramref name="file"/> may have a derived file of <paramref name="kind"/>: one that is plain, of a
        /// type something makes it from. An encrypted file has none, even when a plain file holds the same bytes.
        /// </summary>
        public bool MayHave(FileRecord file, DerivedKind kind)
        {
            return !file.Encrypted && For(kind, [ContentTypes.MediaTypeOf(file.ContentType)]) != null;
        }

        /// <summary>
        /// What makes <paramref name="kind"/> from contents that files of <paramref name="mediaTypes"/> hold; null
        /// when nothing does. One plugin whatever file is asked about, since identical contents have one derived
        /// file of a kind: one that takes one of the types exactly before one that takes its family, then the
        /// first by id.
        /// </summary>
        public KindMaker? For(DerivedKind kind, IReadOnlyCollection<string> mediaTypes)
        {
            return MakersOf(kind)
                .Where(maker => mediaTypes.Any(maker.Declaration.MediaTypes.Includes))
                .OrderBy(maker => mediaTypes.Any(maker.Declaration.MediaTypes.Names) ? 0 : 1)
                .ThenBy(maker => maker.Plugin.Id, StringComparer.Ordinal)
                .FirstOrDefault();
        }

        /// <summary>
        /// The kinds of <paramref name="group"/> that <paramref name="plugin"/> is what makes from contents files of
        /// <paramref name="mediaTypes"/> hold, and that it has yet to, or made with no room to keep it
        /// (<see cref="DerivedOutcome.NoRoom"/>).
        /// </summary>
        public IReadOnlyList<KindMaker> MissingOf(DerivedFilePlugin plugin, MakingGroup group, string sha256, IReadOnlyCollection<string> mediaTypes)
        {
            return DerivedKinds.In(group)
                .Select(kind => For(kind, mediaTypes))
                .OfType<KindMaker>()
                .Where(maker => maker.Plugin.Id == plugin.Id && IsMissing(maker, _store.Find(maker.Declaration.Kind, sha256)))
                .ToList();
        }

        private static bool IsMissing(KindMaker maker, DerivedEntry? entry)
        {
            return !maker.IsCurrent(entry) || entry!.Outcome is DerivedOutcome.NoRoom;
        }

        /// <summary>Whether <paramref name="entry"/> is what an installed plugin made, or failed to make, at the version it makes <paramref name="kind"/> at.</summary>
        public bool IsCurrent(DerivedKind kind, DerivedEntry entry)
        {
            return MakersOf(kind).Any(maker => maker.IsCurrent(entry));
        }

        /// <summary>Whether an installed plugin with the id <paramref name="pluginId"/> makes <paramref name="kind"/>, at any version.</summary>
        public bool Makes(DerivedKind kind, string pluginId)
        {
            return MakersOf(kind).Any(maker => maker.Plugin.Id == pluginId);
        }

        /// <summary>The media types of the files installed plugins make <paramref name="kind"/> from.</summary>
        public MediaTypeSet MediaTypesOf(DerivedKind kind)
        {
            return MediaTypeSet.Union(MakersOf(kind).Select(maker => maker.Declaration.MediaTypes));
        }

        private IEnumerable<KindMaker> MakersOf(DerivedKind kind)
        {
            return _plugins.All
                .SelectMany(plugin => plugin.Makes.Select(declaration => new KindMaker(plugin, declaration)))
                .Where(maker => maker.Declaration.Kind == kind);
        }
    }
}
