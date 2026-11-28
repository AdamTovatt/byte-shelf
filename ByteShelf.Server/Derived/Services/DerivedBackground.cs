using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Plugins.Processes;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Repositories;

namespace ByteShelf.Server.Derived.Services
{
    /// <summary>Makes the groups made in the background (<see cref="DerivedKinds.InBackground"/>): of contents just stored, and of those stored already.</summary>
    internal sealed class DerivedBackground
    {
        /// <summary>How many contents one query lists.</summary>
        internal const int BatchSize = 500;

        private readonly IPlainContentsRepository _contents;
        private readonly DerivedMakers _makers;
        private readonly DerivedMaking _making;
        private readonly DerivedStore _store;
        private readonly DerivedRoom _room;

        public DerivedBackground(IPlainContentsRepository contents, DerivedMakers makers, DerivedMaking making, DerivedStore store, DerivedRoom room)
        {
            _contents = contents;
            _makers = makers;
            _making = making;
            _store = store;
            _room = room;
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
            MediaTypeSet mediaTypes = MediaTypeSet.Union(plugin.MakesIn(group).Select(declaration => declaration.MediaTypes));

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

        /// <summary>Has <paramref name="plugin"/> make what is missing of <paramref name="group"/> of contents just stored.</summary>
        /// <exception cref="PluginUnavailableException">The plugin could not run.</exception>
        public async Task MakeStoredAsync(DerivedFilePlugin plugin, MakingGroup group, string sha256, CancellationToken cancellationToken)
        {
            Lazy<Task<DerivedRoom.RoomNow>> room = new Lazy<Task<DerivedRoom.RoomNow>>(_room.MeasureAsync);
            await MakeMissingAsync(plugin, group, sha256, await _contents.GetMediaTypesAsync(sha256), PluginPriority.Stored, room, cancellationToken);
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
