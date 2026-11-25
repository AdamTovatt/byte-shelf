using EasyReasy.Database;

namespace ByteShelf.Server.Storage.Services
{
    /// <summary>Starts the transactions that may change which contents files hold.</summary>
    public sealed class ContentsSessions
    {
        private readonly IDbSessionFactory _sessionFactory;
        private readonly IContentsTraces _traces;
        private readonly IReadOnlyList<IStoredContentsListener> _listeners;

        public ContentsSessions(IDbSessionFactory sessionFactory, IContentsTraces traces, IEnumerable<IStoredContentsListener> listeners)
        {
            _sessionFactory = sessionFactory;
            _traces = traces;
            _listeners = listeners.ToList();
        }

        public async Task<ContentsSession> CreateAsync()
        {
            return new ContentsSession(await _sessionFactory.CreateSessionWithTransactionAsync(), _traces, _listeners);
        }
    }
}
