using ByteShelf.Server.Derived.Repositories;
using EasyReasy.Database;

namespace ByteShelf.Server.Derived.Services
{
    /// <inheritdoc />
    internal sealed class DerivedRemoval : IDerivedRemoval
    {
        private readonly DerivedStore _store;
        private readonly ISourceContentsRepository _contents;
        private readonly DerivedMaking _making;

        public DerivedRemoval(DerivedStore store, ISourceContentsRepository contents, DerivedMaking making)
        {
            _store = store;
            _contents = contents;
            _making = making;
        }

        /// <inheritdoc />
        public async Task CommitReleasingAsync(IReadOnlyCollection<string> released, IDbTransactionSession session, Action? beforeCommit = null)
        {
            List<IDisposable> held = new List<IDisposable>();
            try
            {
                foreach (string sha256 in released)
                {
                    held.Add(await _store.LockAsync(sha256));
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
            using (await _store.LockAsync(sha256, cancellationToken))
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

            await _making.StopAsync(sha256);
            _store.Delete(sha256);
            return true;
        }
    }
}
