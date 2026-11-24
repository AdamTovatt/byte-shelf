using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Derived.Repositories;
using ByteShelf.Server.Storage.Repositories;
using EasyReasy.Database;

namespace ByteShelf.Server.Derived.Services
{
    /// <summary>
    /// Whether the shelves that hold some contents have room for what is made of them, which counts in the quota of every
    /// shelf with a file row, live or in the trash, that holds them, once a row, as a file's own bytes do.
    /// </summary>
    internal sealed class DerivedRoom
    {
        private readonly IShelfRepository _shelves;
        private readonly IFileRepository _files;
        private readonly IDerivedSizeRepository _sizes;

        public DerivedRoom(IShelfRepository shelves, IFileRepository files, IDerivedSizeRepository sizes)
        {
            _shelves = shelves;
            _files = files;
            _sizes = sizes;
        }

        /// <summary>
        /// Weighs what was made of the contents in one run, each kind in turn beside those before it: one made is kept only
        /// if every such shelf has room for what it adds, its bytes less those of the one it replaces, and is otherwise
        /// answered as made with no room for it (<see cref="DerivedOutcome.NoRoom"/>). One that adds nothing is kept
        /// whatever the room. Asked in the transaction that keeps them.
        /// </summary>
        public async Task<IReadOnlyList<KeptFile>> WeighAsync(string sha256, IReadOnlyList<KeptFile> made, IDbTransactionSession session)
        {
            long more = 0;
            List<KeptFile> weighed = [];
            foreach (KeptFile file in made)
            {
                // What it replaces goes whether or not it is kept.
                long had = await _sizes.GetAsync(sha256, file.Kind, session) ?? 0;
                if (file.SourcePath == null)
                {
                    more -= had;
                    weighed.Add(file);
                    continue;
                }

                long bytes = new FileInfo(file.SourcePath).Length;
                if (bytes <= had || await _shelves.HasRoomForMoreOfAsync(sha256, more + bytes - had, session))
                {
                    more += bytes - had;
                    weighed.Add(file);
                }
                else
                {
                    more -= had;
                    weighed.Add(new KeptFile(file.Kind, file.Entry with { Outcome = new DerivedOutcome.NoRoom(bytes) }, SourcePath: null));
                }
            }

            return weighed;
        }

        /// <summary>
        /// Whether the shelves would have room for the derived file <paramref name="entry"/> is about, were it made again:
        /// always, unless it was made with none, and then whether they have room for as many bytes now.
        /// </summary>
        public async Task<bool> HasRoomForAsync(string sha256, DerivedEntry? entry)
        {
            return entry?.Outcome is not DerivedOutcome.NoRoom noRoom || await _shelves.HasRoomForMoreOfAsync(sha256, noRoom.Bytes);
        }

        /// <summary>
        /// The room every shelf with a quota has now, measured once for weighing many contents ahead of making what is
        /// missing of them: what is made is weighed again as it is kept, so one the room went to meanwhile is not kept.
        /// </summary>
        public async Task<RoomNow> MeasureAsync()
        {
            return new RoomNow(await _shelves.GetFreeBytesAsync(), _files);
        }

        /// <summary>The room shelves had as it was measured (<see cref="MeasureAsync"/>).</summary>
        internal sealed class RoomNow
        {
            private readonly IReadOnlyDictionary<Guid, long> _free;
            private readonly IFileRepository _files;

            public RoomNow(IReadOnlyDictionary<Guid, long> free, IFileRepository files)
            {
                _free = free;
                _files = files;
            }

            /// <summary>As <see cref="HasRoomForAsync"/>, by the room measured, asking only which shelves hold the contents.</summary>
            public async Task<bool> HasRoomForAsync(string sha256, DerivedEntry? entry)
            {
                if (entry?.Outcome is not DerivedOutcome.NoRoom noRoom || _free.Count == 0)
                {
                    return true;
                }

                foreach ((Guid shelfId, int rows) in await _files.CountByShelfWithObjectAsync(sha256))
                {
                    if (_free.TryGetValue(shelfId, out long free) && free < noRoom.Bytes * rows)
                    {
                        return false;
                    }
                }

                return true;
            }
        }
    }
}
