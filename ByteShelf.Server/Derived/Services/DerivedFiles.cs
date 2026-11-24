using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Derived.Repositories;
using ByteShelf.Server.Plugins.Processes;
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

        /// <summary>
        /// Removes the least recently used <see cref="DerivedKind.Full"/> files until the rest take at most
        /// <see cref="DerivedStore.MaxFullBytes"/> together (<see cref="DerivedStore.GetFullOverTheLimit"/>), but none whose
        /// lock is held, as it is in use: this waits for nobody. Answers how many. Hold no lock.
        /// </summary>
        public async Task<int> TrimFullAsync()
        {
            int removed = 0;
            foreach (string sha256 in _store.GetFullOverTheLimit())
            {
                removed += await _transactions.TryRunAsync(sha256, session => DeleteAsync(DerivedKind.Full, sha256, session)) ? 1 : 0;
            }

            return removed;
        }
    }
}
