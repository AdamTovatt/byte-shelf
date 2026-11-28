using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Plugins.Processes;
using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Repositories;
using ByteShelf.Server.Storage.Services;
using ByteShelfCommon;

namespace ByteShelf.Server.Derived.Services
{
    /// <summary>The derived files of files, made as they are asked for when they are missing.</summary>
    internal sealed class DerivedFileService : IDerivedFileService
    {
        /// <summary>How long a request waits for a derived file to be made.</summary>
        private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

        /// <summary>When a request that waited is told to come back.</summary>
        private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(5);

        /// <summary>When a request for a kind only made in the background, which may take hours, is told to come back.</summary>
        private static readonly TimeSpan BackgroundRetryAfter = TimeSpan.FromMinutes(1);

        private readonly ShelfTree _shelfTree;
        private readonly ITrashService _trashService;
        private readonly IPlainContentsRepository _contents;
        private readonly DerivedMakers _makers;
        private readonly DerivedStore _store;
        private readonly ContentsLocks _locks;
        private readonly DerivedMaking _making;
        private readonly TimeProvider _timeProvider;
        private readonly DerivedRoom _room;
        private readonly DerivedBackground _background;

        public DerivedFileService(
            ShelfTree shelfTree,
            ITrashService trashService,
            IPlainContentsRepository contents,
            DerivedMakers makers,
            DerivedStore store,
            ContentsLocks locks,
            DerivedMaking making,
            TimeProvider timeProvider,
            DerivedRoom room,
            DerivedBackground background)
        {
            _room = room;
            _background = background;
            _shelfTree = shelfTree;
            _trashService = trashService;
            _contents = contents;
            _makers = makers;
            _store = store;
            _locks = locks;
            _making = making;
            _timeProvider = timeProvider;
        }

        /// <inheritdoc />
        public async Task<DerivedFile> GetAsync(Shelf shelf, EntryReference reference, DerivedKind kind, CancellationToken cancellationToken)
        {
            return await OfAsync(await _shelfTree.GetFileAsync(shelf.Id, reference), kind, cancellationToken);
        }

        /// <inheritdoc />
        public async Task<DerivedFile> GetDeletedAsync(Shelf shelf, Guid deletionId, Guid fileId, DerivedKind kind, CancellationToken cancellationToken)
        {
            return await OfAsync(await _trashService.GetDeletedFileAsync(shelf, deletionId, fileId), kind, cancellationToken);
        }

        private async Task<DerivedFile> OfAsync(FileRecord file, DerivedKind kind, CancellationToken cancellationToken)
        {
            if (!_makers.MayHave(file, kind))
            {
                throw None(file, kind);
            }

            // By every type the contents are held as, so whichever file is asked about they have one maker.
            KindMaker maker = _makers.For(kind, await _contents.GetMediaTypesAsync(file.Sha256)) ?? throw None(file, kind);
            if (await FindAsync(file.Sha256, maker, cancellationToken) is { } found)
            {
                return found.File ?? throw None(file, kind);
            }

            MakingGroup group = MakingGroup.Of(kind);
            if (!group.Start.MadeWhenAsked)
            {
                throw await _background.MakingOfAsync(maker.Plugin, group, file.Sha256) switch
                {
                    BackgroundMaking.None => None(file, kind),
                    BackgroundMaking.CannotRun => new PluginUnavailableException($"its maker could not do its {group.Lane.Name} runs when it last had one to do"),
                    _ => Later(kind, BackgroundRetryAfter),
                };
            }

            Task<MakingOutcome> making = _making.MakeAsync(file.Sha256, kind, maker.Plugin, PluginPriority.Asked);
            try
            {
                if (await making.WaitAsync(WaitLimit, _timeProvider, cancellationToken) == MakingOutcome.Stopped)
                {
                    throw Later(kind);
                }
            }
            catch (TimeoutException)
            {
                throw Later(kind);
            }

            return (await FindAsync(file.Sha256, maker, cancellationToken))?.File ?? throw None(file, kind);
        }

        /// <summary>What <paramref name="maker"/> made of the contents, with no file when it could not; null when it has yet to.</summary>
        private async Task<Found?> FindAsync(string sha256, KindMaker maker, CancellationToken cancellationToken)
        {
            DerivedKind kind = maker.Declaration.Kind;
            using (await _locks.LockAsync(sha256, cancellationToken))
            {
                DerivedEntry? entry = _store.Find(kind, sha256);
                if (!maker.IsCurrent(entry))
                {
                    return null;
                }

                switch (entry!.Outcome)
                {
                    case DerivedOutcome.NoRoom:
                        return await _room.HasRoomForAsync(sha256, entry) ? null : throw NoRoom(kind);
                    case DerivedOutcome.Made made:
                        return _store.TryOpenRead(kind, sha256) is not FileStream contents
                            ? null
                            : new Found(new DerivedFile
                            {
                                Kind = kind,
                                Sha256 = sha256,
                                Tag = entry.Tag,
                                MediaType = made.MediaType,
                                Contents = contents,
                            });
                    default:
                        return new Found(null);
                }
            }
        }

        private static NotFoundException None(FileRecord file, DerivedKind kind)
        {
            return new NotFoundException($"The file {file.Id} has no {DerivedKinds.NameOf(kind)}.");
        }

        private static QuotaExceededException NoRoom(DerivedKind kind)
        {
            return new QuotaExceededException($"A shelf that holds the file's contents has no room for its {DerivedKinds.NameOf(kind)}.");
        }

        private static RetryLaterException Later(DerivedKind kind, TimeSpan? after = null)
        {
            return new RetryLaterException($"The {DerivedKinds.NameOf(kind)} is still being made; ask again.", after ?? RetryAfter);
        }

        private sealed record Found(DerivedFile? File);
    }
}
