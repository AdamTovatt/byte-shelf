using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Derived.Repositories;
using ByteShelf.Server.Storage.Services;
using ByteShelfCommon;
using EasyReasy.Database;

namespace ByteShelf.Server.Derived.Services
{
    /// <summary>
    /// Keeps and removes derived files (<see cref="DerivedStore"/>) with the bytes each takes (<see cref="IDerivedSizeRepository"/>),
    /// for shelves' quotas: the sizes written in the caller's transaction, begun before the contents' lock
    /// (<see cref="ContentsTransactions"/>), and committed once the folder has changed. What fails in between leaves the
    /// folder for maintenance to count again.
    /// </summary>
    internal sealed class DerivedFiles
    {
        private readonly DerivedStore _store;
        private readonly IDerivedSizeRepository _sizes;
        private readonly DerivedRoom _room;
        private readonly ContentsTransactions _transactions;

        public DerivedFiles(DerivedStore store, IDerivedSizeRepository sizes, DerivedRoom room, ContentsTransactions transactions)
        {
            _store = store;
            _sizes = sizes;
            _room = room;
            _transactions = transactions;
        }

        /// <summary>
        /// Keeps what was made of the contents in one run, each file made only where the shelves that hold the contents have
        /// room for it (<see cref="DerivedRoom.WeighAsync"/>); answers what it kept of each. Hold the lock.
        /// </summary>
        public async Task<IReadOnlyList<KeptFile>> KeepAsync(string sha256, IReadOnlyList<KeptFile> made, IDbTransactionSession session)
        {
            IReadOnlyList<KeptFile> weighed = await _room.WeighAsync(sha256, made, session);
            foreach (KeptFile file in weighed)
            {
                if (file.SourcePath == null)
                {
                    await _sizes.RemoveAsync(sha256, file.Kind, session);
                }
                else
                {
                    await _sizes.SetAsync(sha256, file.Kind, new FileInfo(file.SourcePath).Length, session);
                }
            }

            _store.Put(sha256, weighed);
            return weighed;
        }

        /// <summary>Removes one derived file, or what is known of making it, and its size. Hold the lock.</summary>
        public async Task DeleteAsync(DerivedKind kind, string sha256, IDbSession session)
        {
            await _sizes.RemoveAsync(sha256, kind, session);
            _store.Delete(kind, sha256);
        }

        /// <summary>Removes everything derived from the contents, and their sizes. Hold the lock.</summary>
        public async Task DeleteAsync(string sha256, IDbSession session)
        {
            await _sizes.RemoveAsync(sha256, session);
            _store.Delete(sha256);
        }

        /// <summary>Writes the size of the derived file of the kind as the folder holds it; answers whether that changed it. Hold the lock.</summary>
        public async Task<bool> CountAgainAsync(DerivedKind kind, string sha256, IDbSession session)
        {
            long? size = _store.SizeOf(kind, sha256);
            if (size == await _sizes.GetAsync(sha256, kind, session))
            {
                return false;
            }

            if (size is long counted)
            {
                await _sizes.SetAsync(sha256, kind, counted, session);
            }
            else
            {
                await _sizes.RemoveAsync(sha256, kind, session);
            }

            return true;
        }

        /// <summary>Writes the sizes of everything derived from the contents as the folder holds it, in a transaction of its own. Hold no lock.</summary>
        public Task CountAgainAsync(string sha256)
        {
            return _transactions.RunAsync(sha256, async session =>
            {
                foreach (DerivedKind kind in DerivedKinds.All)
                {
                    await CountAgainAsync(kind, sha256, session);
                }

                return true;
            });
        }

        /// <summary>
        /// Removes the least recently used <see cref="DerivedKind.Full"/> files until the rest take at most
        /// <see cref="DerivedStore.MaxFullBytes"/> together (<see cref="DerivedStore.GetFullOverTheLimit"/>), but none whose
        /// lock is held, as it is in use, or that was used since it was listed: this waits for nobody. Answers how many.
        /// Hold no lock.
        /// </summary>
        public async Task<int> TrimFullAsync()
        {
            int removed = 0;
            foreach (FullFile full in _store.GetFullOverTheLimit())
            {
                removed += await _transactions.TryRunAsync(full.Sha256, async session =>
                {
                    // Used since it was listed, it is no longer the least recently used.
                    if (_store.LastUsedOfFull(full.Sha256) != full.LastUsedUtc)
                    {
                        return false;
                    }

                    await DeleteAsync(DerivedKind.Full, full.Sha256, session);
                    return true;
                }) ? 1 : 0;
            }

            return removed;
        }
    }
}
