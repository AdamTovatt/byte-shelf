using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Derived.Repositories;
using ByteShelf.Server.Plugins.Processes;
using ByteShelf.Server.Storage.Repositories;
using ByteShelf.Server.Storage.Services;
using ByteShelfCommon;

namespace ByteShelf.Server.Derived.Services
{
    /// <summary>
    /// One pass of the job <c>derived.maintenance</c>: removes the derived files of contents no plain file holds
    /// any more (a crash leaves them, as does a removal that failed), those no installed plugin makes, and
    /// the least used over their limit, and counts the sizes of the rest again for quota; then what else plugins'
    /// processes have of contents no plain file holds.
    /// </summary>
    internal sealed class DerivedMaintenance
    {
        private const int BatchSize = 500;

        private readonly DerivedStore _store;
        private readonly DerivedFiles _files;
        private readonly IDerivedSizeRepository _sizes;
        private readonly IPlainContentsRepository _contents;
        private readonly IContentsTraces _traces;
        private readonly DerivedMakers _makers;
        private readonly ContentsTransactions _transactions;
        private readonly PluginProcesses _processes;

        public DerivedMaintenance(
            DerivedStore store,
            DerivedFiles files,
            IDerivedSizeRepository sizes,
            IPlainContentsRepository contents,
            IContentsTraces traces,
            DerivedMakers makers,
            ContentsTransactions transactions,
            PluginProcesses processes)
        {
            _store = store;
            _files = files;
            _sizes = sizes;
            _contents = contents;
            _traces = traces;
            _makers = makers;
            _transactions = transactions;
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

            int trimmed = await _files.TrimFullAsync();
            int recounted = await RecountAsync(cancellationToken);

            // Last, as a process that will not die fails the pass.
            foreach (string[] batch in _processes.GetContentsGiven().Chunk(BatchSize))
            {
                unheld += await RemoveUnheldAsync(await _contents.GetUnheldAsync(batch), cancellationToken);
            }

            return new DerivedMaintenanceSummary(unheld, orphaned, trimmed, recounted);
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
            // Asked first without the database, as most are made by what is installed.
            if (IsMade(kind, sha256))
            {
                return false;
            }

            return await _transactions.RunAsync(sha256, async session =>
            {
                if (IsMade(kind, sha256))
                {
                    return false;
                }

                await _files.DeleteAsync(kind, sha256, session);
                return true;
            }, cancellationToken);
        }

        private bool IsMade(DerivedKind kind, string sha256)
        {
            return _store.Find(kind, sha256) is { } entry && _makers.Makes(kind, entry.Plugin);
        }

        /// <summary>
        /// Brings the sizes written in line with the derived files on disk, so an emptied or edited folder counts what is
        /// there; answers how many it changed. Each is asked again under its contents' lock, as it may have been kept or
        /// removed since.
        /// </summary>
        private async Task<int> RecountAsync(CancellationToken cancellationToken)
        {
            IReadOnlyDictionary<(string Sha256, DerivedKind Kind), long> written = await _sizes.GetAllAsync();
            IEnumerable<(string Sha256, DerivedKind Kind)> onDisk = DerivedKinds.All.SelectMany(kind => _store.EnumerateHashes(kind).Select(sha256 => (sha256, kind)));

            int changed = 0;
            foreach ((string sha256, DerivedKind kind) in written.Keys.Concat(onDisk).Distinct().ToList())
            {
                long? had = written.TryGetValue((sha256, kind), out long bytes) ? bytes : null;
                if (_store.SizeOf(kind, sha256) == had)
                {
                    continue;
                }

                changed += await _transactions.RunAsync(sha256, async session =>
                {
                    long? size = _store.SizeOf(kind, sha256);
                    if (size == await _sizes.GetAsync(sha256, kind, session))
                    {
                        return 0;
                    }

                    if (size is long counted)
                    {
                        await _sizes.SetAsync(sha256, kind, counted, session);
                    }
                    else
                    {
                        await _sizes.RemoveAsync(sha256, kind, session);
                    }

                    return 1;
                }, cancellationToken);
            }

            return changed;
        }
    }
}
