using System.Data.Common;
using ByteShelf.Server.Storage.Repositories;
using EasyReasy.Database;

namespace ByteShelf.Server.Storage.Services
{
    /// <inheritdoc />
    /// <remarks>
    /// Traces are removed inside the transaction, not before it, though what holds some may take ten seconds to end and
    /// the database's write lock is held meanwhile: before it, a swap that is refused would end it for nothing.
    /// </remarks>
    public sealed class ContentsSession : IContentsSession
    {
        private readonly IDbTransactionSession _session;
        private readonly IContentsTraces _traces;
        private readonly IReadOnlyList<IStoredContentsListener> _listeners;
        private readonly HashSet<string> _released = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _stored = new HashSet<string>(StringComparer.Ordinal);

        internal ContentsSession(IDbTransactionSession session, IContentsTraces traces, IReadOnlyList<IStoredContentsListener> listeners)
        {
            _session = session;
            _traces = traces;
            _listeners = listeners;
        }

        /// <summary>One over a database that is a copy, of whose contents nobody has anything.</summary>
        public static ContentsSession OfACopy(IDbTransactionSession session)
        {
            return new ContentsSession(session, new NoTraces(), []);
        }

        /// <summary>The contents released: once it has committed, and its caller holds no object's lock, their objects go (<see cref="ObjectCollector.RemoveReleasedAsync"/>).</summary>
        public IReadOnlyCollection<string> Released => _released;

        public DbConnection Connection => _session.Connection;

        public DbTransaction Transaction => _session.Transaction;

        DbTransaction? IDbSession.Transaction => _session.Transaction;

        public void Release(string sha256)
        {
            _released.Add(sha256);
        }

        public void Store(string sha256)
        {
            _stored.Add(sha256);
        }

        /// <summary>Commits once nothing else the server has of the contents released is left, and none comes to be until it has.</summary>
        /// <exception cref="TracesNotRemovedException">Something is; nothing is committed.</exception>
        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            return CommitAsync(() => { }, cancellationToken);
        }

        /// <inheritdoc cref="CommitAsync(CancellationToken)"/>
        /// <param name="beforeCommit">What cannot be undone, done once nothing but the commit is left.</param>
        /// <param name="cancellationToken">Cancels the commit.</param>
        public async Task CommitAsync(Action beforeCommit, CancellationToken cancellationToken = default)
        {
            List<IDisposable> held = new List<IDisposable>();
            try
            {
                foreach (string sha256 in _released)
                {
                    held.Add(await _traces.RemoveForCommitAsync(sha256, _session));
                }

                beforeCommit();
                await _session.CommitAsync(cancellationToken);
            }
            finally
            {
                held.ForEach(contents => contents.Dispose());
            }

            foreach (IStoredContentsListener listener in _listeners)
            {
                foreach (string sha256 in _stored)
                {
                    listener.Stored(sha256);
                }
            }
        }

        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            return _session.RollbackAsync(cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            return _session.DisposeAsync();
        }

        private sealed class NoTraces : IContentsTraces, IDisposable
        {
            public Task<IDisposable> RemoveForCommitAsync(string sha256, IDbSession session)
            {
                return Task.FromResult<IDisposable>(this);
            }

            public Task<bool> RemoveUnheldAsync(string sha256, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(true);
            }

            public void Dispose()
            {
            }
        }
    }
}
