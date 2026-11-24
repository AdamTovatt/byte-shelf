using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Plugins.Processes;
using ByteShelf.Server.Storage.Repositories;
using ByteShelf.Server.Storage.Services;
using ByteShelfCommon;

namespace ByteShelf.Server.Derived.Services
{
    /// <summary>
    /// One pass of the job <c>derived.maintenance</c>: removes the derived files of contents no plain file holds
    /// any more (a crash leaves them, as does a removal that failed), those no installed plugin makes, and
    /// the least used over their limit; then what else plugins' processes have of contents no plain file holds.
    /// </summary>
    internal sealed class DerivedMaintenance
    {
        private const int BatchSize = 500;

        private readonly DerivedStore _store;
        private readonly IPlainContentsRepository _contents;
        private readonly IContentsTraces _traces;
        private readonly DerivedMakers _makers;
        private readonly ContentsLocks _locks;
        private readonly PluginProcesses _processes;

        public DerivedMaintenance(
            DerivedStore store,
            IPlainContentsRepository contents,
            IContentsTraces traces,
            DerivedMakers makers,
            ContentsLocks locks,
            PluginProcesses processes)
        {
            _store = store;
            _contents = contents;
            _traces = traces;
            _makers = makers;
            _locks = locks;
            _processes = processes;
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
                    unheld += await RemoveUnheldAsync(unheldHere, cancellationToken);
                    foreach (string sha256 in batch.Except(unheldHere, StringComparer.Ordinal))
                    {
                        orphaned += await RemoveOrphanedAsync(kind, sha256, cancellationToken) ? 1 : 0;
                    }
                }
            }

            int trimmed = _store.TrimFull();

            // Last, as a process that will not die fails the pass.
            foreach (string[] batch in _processes.GetContentsGiven().Chunk(BatchSize))
            {
                unheld += await RemoveUnheldAsync(await _contents.GetUnheldAsync(batch), cancellationToken);
            }

            return new DerivedMaintenanceSummary(unheld, orphaned, trimmed);
        }

        private async Task<int> RemoveUnheldAsync(IReadOnlyList<string> unheld, CancellationToken cancellationToken)
        {
            int removed = 0;
            foreach (string sha256 in unheld)
            {
                removed += await _traces.RemoveUnheldAsync(sha256, cancellationToken) ? 1 : 0;
            }

            return removed;
        }

        private async Task<bool> RemoveOrphanedAsync(DerivedKind kind, string sha256, CancellationToken cancellationToken)
        {
            using (await _locks.LockAsync(sha256, cancellationToken))
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
