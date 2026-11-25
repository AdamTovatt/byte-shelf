using ByteShelf.Server.Plugins.Processes;
using ByteShelf.Server.Storage.Repositories;
using ByteShelf.Server.Storage.Services;
using EasyReasy.Database;

namespace ByteShelf.Server.Derived.Services
{
    /// <inheritdoc />
    /// <remarks>What plugins have of them: the files derived from them, and every process that read them, for metadata too.</remarks>
    internal sealed class ContentsTraces : IContentsTraces
    {
        private readonly DerivedFiles _files;
        private readonly ContentsLocks _locks;
        private readonly ContentsTransactions _transactions;
        private readonly IPlainContentsRepository _contents;
        private readonly PluginProcesses _processes;

        public ContentsTraces(DerivedFiles files, ContentsLocks locks, ContentsTransactions transactions, IPlainContentsRepository contents, PluginProcesses processes)
        {
            _files = files;
            _locks = locks;
            _transactions = transactions;
            _contents = contents;
            _processes = processes;
        }

        /// <inheritdoc />
        public async Task<IDisposable> RemoveForCommitAsync(string sha256, IDbSession session)
        {
            IDisposable held = await _locks.LockAsync(sha256);
            try
            {
                await RemoveUnheldAsync(sha256, session);
                return held;
            }
            catch
            {
                held.Dispose();
                throw;
            }
        }

        /// <inheritdoc />
        public Task ReconcileAsync(string sha256)
        {
            return _files.CountAgainAsync(sha256);
        }

        /// <inheritdoc />
        public Task<bool> RemoveUnheldAsync(string sha256, CancellationToken cancellationToken = default)
        {
            return _transactions.RunAsync(sha256, session => RemoveUnheldAsync(sha256, session), cancellationToken);
        }

        private async Task<bool> RemoveUnheldAsync(string sha256, IDbSession session)
        {
            if ((await _contents.GetUnheldAsync([sha256], session)).Count == 0)
            {
                return false;
            }

            try
            {
                await _processes.StopAsync(sha256);
                await _files.DeleteAsync(sha256, session);
                return true;
            }
            catch (Exception exception) when (exception is PluginNotStoppedException or IOException or UnauthorizedAccessException)
            {
                throw new TracesNotRemovedException(sha256, exception);
            }
        }
    }
}
