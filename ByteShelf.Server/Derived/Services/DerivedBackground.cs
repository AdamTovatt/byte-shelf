using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Plugins.Models;
using ByteShelf.Server.Plugins.Processes;
using ByteShelf.Server.Plugins.Services;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Repositories;
using ByteShelf.Server.Storage.Services;
using Microsoft.Win32.SafeHandles;

namespace ByteShelf.Server.Derived.Services
{
    /// <summary>Makes the groups made in the background (<see cref="MakingGroup.InBackground"/>): of contents just stored, and of those stored already.</summary>
    internal sealed class DerivedBackground
    {
        /// <summary>How many contents one query lists.</summary>
        internal const int BatchSize = 500;

        private readonly IPlainContentsRepository _contents;
        private readonly DerivedMakers _makers;
        private readonly DerivedMaking _making;
        private readonly DerivedStore _store;
        private readonly DerivedRoom _room;
        private readonly PluginProblems _problems;
        private readonly ObjectStore _objectStore;

        public DerivedBackground(IPlainContentsRepository contents, DerivedMakers makers, DerivedMaking making, DerivedStore store, DerivedRoom room, PluginProblems problems, ObjectStore objectStore)
        {
            _contents = contents;
            _makers = makers;
            _making = making;
            _store = store;
            _room = room;
            _problems = problems;
            _objectStore = objectStore;
        }

        /// <summary>
        /// The plugins with each group they make in the background (<see cref="MakingGroup.InBackground"/>), the one rule of
        /// it, which the stored contents' queues and the backlog's passes go by: each goes at its own pace.
        /// </summary>
        internal static IEnumerable<(DerivedFilePlugin Plugin, MakingGroup Group)> MakersOf(IDerivedFilePlugins plugins)
        {
            return plugins.All.SelectMany(plugin => MakingGroup.InBackground.Where(plugin.MakesAny).Select(group => (plugin, group)));
        }

        /// <summary>
        /// Has <paramref name="plugin"/> make what is missing of <paramref name="group"/> of every contents a live plain
        /// file holds, whenever it was stored; answers how many contents it ran for.
        /// </summary>
        /// <remarks>
        /// Looks on disk for every contents' files each time, with no index of what is missing: a cache somebody
        /// emptied is made again, which an index beside it would not notice.
        /// </remarks>
        /// <exception cref="PluginUnavailableException">The plugin could not run.</exception>
        public async Task<int> MakeBacklogAsync(DerivedFilePlugin plugin, MakingGroup group, CancellationToken cancellationToken)
        {
            MediaTypeSet mediaTypes = TakenBy(plugin, group);

            // Measured once for the pass, and only once some contents need it.
            Lazy<Task<DerivedRoom.RoomNow>> room = new Lazy<Task<DerivedRoom.RoomNow>>(_room.MeasureAsync);
            int made = 0;
            string? after = null;
            while (await _contents.GetLiveAsync(mediaTypes, after, BatchSize) is { Count: > 0 } batch)
            {
                foreach ((string sha256, IReadOnlyList<string> held) in batch)
                {
                    after = sha256;
                    made += await MakeMissingAsync(plugin, group, sha256, held, PluginPriority.Backlog, room, cancellationToken);
                }
            }

            return made;
        }

        /// <summary>
        /// What becomes of <paramref name="group"/> of <paramref name="sha256"/>, missing, by <paramref name="plugin"/>:
        /// none is made unless its backlog takes those contents, which it does while a live plain file of a type the
        /// plugin makes the group from holds them and they can be read, nor while the plugin's runs in the group's lane
        /// could not run.
        /// </summary>
        public async Task<BackgroundMaking> MakingOfAsync(DerivedFilePlugin plugin, MakingGroup group, string sha256)
        {
            if (!await _contents.IsLiveAsync(sha256, TakenBy(plugin, group)) || !CanRead(sha256))
            {
                return BackgroundMaking.None;
            }

            return _problems.Of(plugin.Id, PluginPart.Maker, group.Lane) != null ? BackgroundMaking.CannotRun : BackgroundMaking.Later;
        }

        /// <summary>Has <paramref name="plugin"/> make what is missing of <paramref name="group"/> of contents just stored.</summary>
        /// <exception cref="PluginUnavailableException">The plugin could not run.</exception>
        public async Task MakeStoredAsync(DerivedFilePlugin plugin, MakingGroup group, string sha256, CancellationToken cancellationToken)
        {
            Lazy<Task<DerivedRoom.RoomNow>> room = new Lazy<Task<DerivedRoom.RoomNow>>(_room.MeasureAsync);
            await MakeMissingAsync(plugin, group, sha256, await _contents.GetMediaTypesAsync(sha256), PluginPriority.Stored, room, cancellationToken);
        }

        /// <summary>
        /// Whether the contents' start can be read, as a plugin reads them: of those that cannot nothing is made, and
        /// nothing is kept of that, as they may be read again later, so it is asked of them each time.
        /// </summary>
        private bool CanRead(string sha256)
        {
            try
            {
                using SafeFileHandle contents = File.OpenHandle(_objectStore.GetPath(sha256));
                RandomAccess.Read(contents, stackalloc byte[1], 0);
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>The types of files whose contents <paramref name="plugin"/> makes <paramref name="group"/> of.</summary>
        private static MediaTypeSet TakenBy(DerivedFilePlugin plugin, MakingGroup group)
        {
            return MediaTypeSet.Union(plugin.MakesIn(group).Select(declaration => declaration.MediaTypes));
        }

        private async Task<int> MakeMissingAsync(
            DerivedFilePlugin plugin,
            MakingGroup group,
            string sha256,
            IReadOnlyList<string> mediaTypes,
            PluginPriority priority,
            Lazy<Task<DerivedRoom.RoomNow>> room,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<KindMaker> missing = _makers.MissingOf(plugin, group, sha256, mediaTypes);
            if (missing.Count == 0 || !await AnyHasRoomAsync(sha256, missing, room))
            {
                return 0;
            }

            MakingOutcome outcome = await _making.MakeAsync(sha256, missing[0].Declaration.Kind, plugin, priority).WaitAsync(cancellationToken);
            return outcome == MakingOutcome.Kept ? 1 : 0;
        }

        /// <summary>
        /// Whether one of <paramref name="missing"/> may be kept once made: one not made with no room for it, as most are
        /// not, or one there is room for now, as measured.
        /// </summary>
        private async Task<bool> AnyHasRoomAsync(string sha256, IReadOnlyList<KindMaker> missing, Lazy<Task<DerivedRoom.RoomNow>> room)
        {
            List<DerivedEntry?> entries = missing.Select(maker => _store.Find(maker.Declaration.Kind, sha256)).ToList();
            if (entries.Any(entry => entry?.Outcome is not DerivedOutcome.NoRoom))
            {
                return true;
            }

            DerivedRoom.RoomNow now = await room.Value;
            foreach (DerivedEntry? entry in entries)
            {
                if (await now.HasRoomForAsync(sha256, entry))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
