using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Derived.Repositories;
using ByteShelfCommon;

namespace ByteShelf.Server.Derived.Services
{
    /// <summary>
    /// One pass of the job <c>derived.maintenance</c>: removes the derived files of contents no plain file holds
    /// any more (a crash leaves them, as does a shelf removed on a replica), those no installed plugin makes, and
    /// the least used over their limit.
    /// </summary>
    internal sealed class DerivedMaintenance
    {
        private const int BatchSize = 500;

        private readonly DerivedStore _store;
        private readonly ISourceContentsRepository _contents;
        private readonly IDerivedRemoval _removal;
        private readonly DerivedMakers _makers;

        public DerivedMaintenance(DerivedStore store, ISourceContentsRepository contents, IDerivedRemoval removal, DerivedMakers makers)
        {
            _store = store;
            _contents = contents;
            _removal = removal;
            _makers = makers;
        }

        public async Task<DerivedMaintenanceSummary> RunAsync(CancellationToken cancellationToken)
        {
            int unheld = 0;
            int orphaned = 0;
            foreach (DerivedKind kind in DerivedKinds.All)
            {
                foreach (string[] batch in _store.EnumerateHashes(kind).ToList().Chunk(BatchSize))
                {
                    IReadOnlyList<string> unheldHere = await _contents.GetUnheldAsync(batch);
                    foreach (string sha256 in unheldHere)
                    {
                        unheld += await _removal.RemoveUnheldAsync(sha256, cancellationToken) ? 1 : 0;
                    }

                    foreach (string sha256 in batch.Except(unheldHere, StringComparer.Ordinal))
                    {
                        orphaned += await RemoveOrphanedAsync(kind, sha256, cancellationToken) ? 1 : 0;
                    }
                }
            }

            return new DerivedMaintenanceSummary(unheld, orphaned, _store.TrimFull());
        }

        private async Task<bool> RemoveOrphanedAsync(DerivedKind kind, string sha256, CancellationToken cancellationToken)
        {
            using (await _store.LockAsync(sha256, cancellationToken))
            {
                if (_store.Find(kind, sha256) is { } entry && _makers.Makes(kind, entry.Plugin))
                {
                    return false;
                }

                _store.Delete(kind, sha256);
                return true;
            }
        }
    }
}
