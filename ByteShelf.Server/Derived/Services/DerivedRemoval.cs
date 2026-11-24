using ByteShelf.Server.Plugins.Processes;
using ByteShelf.Server.Storage.Repositories;
using EasyReasy.Database;

namespace ByteShelf.Server.Derived.Services
{
    /// <inheritdoc />
    /// <remarks>
    /// Processes are ended inside the transaction, not before it, though one that will not die holds the database's
    /// write lock for some ten seconds: before it, a swap that is refused would end them for nothing.
    /// </remarks>
    internal sealed class DerivedRemoval : IDerivedRemoval
    {
        private readonly DerivedStore _store;
        private readonly ContentsLocks _locks;
        private readonly IPlainContentsRepository _contents;
        private readonly PluginProcesses _processes;

        public DerivedRemoval(DerivedStore store, ContentsLocks locks, IPlainContentsRepository contents, PluginProcesses processes)
        {
            _store = store;
            _locks = locks;
            _contents = contents;
            _processes = processes;
        }

        /// <inheritdoc />
        public async Task CommitReleasingAsync(IReadOnlyCollection<string> released, IDbTransactionSession session, Action? beforeCommit = null)
        {
            List<IDisposable> held = new List<IDisposable>();
            try
            {
                foreach (string sha256 in released)
                {
                    held.Add(await _locks.LockAsync(sha256));
                    await RemoveUnheldAsync(sha256, session);
                }

                beforeCommit?.Invoke();
                await session.CommitAsync();
            }
            finally
            {
                held.ForEach(contents => contents.Dispose());
            }
        }

        /// <inheritdoc />
        public async Task<bool> RemoveUnheldAsync(string sha256, CancellationToken cancellationToken = default)
        {
            using (await _locks.LockAsync(sha256, cancellationToken))
            {
                return await RemoveUnheldAsync(sha256, session: null);
            }
        }

        private async Task<bool> RemoveUnheldAsync(string sha256, IDbSession? session)
        {
            if ((await _contents.GetUnheldAsync([sha256], session)).Count == 0)
            {
                return false;
            }

            await _processes.StopAsync(sha256);
            _store.Delete(sha256);
            return true;
        }
    }
}
