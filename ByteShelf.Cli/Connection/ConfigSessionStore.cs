using ByteShelf.Cli.Config;
using ByteShelfClient.Http;
using ByteShelfCommon;

namespace ByteShelf.Cli.Connection
{
    /// <summary>
    /// One user's session on one server as the config file keeps it, shared by every <c>byteshelf</c>
    /// process. A config that has since been logged out, or logged in elsewhere, keeps no session for it.
    /// </summary>
    internal sealed class ConfigSessionStore : ISharedSessionStore
    {
        private readonly CliConfigStore _store;
        private readonly string _server;
        private readonly string _email;

        public ConfigSessionStore(CliConfigStore store, string server, string email)
        {
            _store = store;
            _server = server;
            _email = email;
        }

        public Task<IAsyncDisposable> LockAsync(CancellationToken cancellationToken)
        {
            return _store.LockAsync(cancellationToken);
        }

        public Task<SessionTokens?> LoadAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(Kept(_store.Load()));
        }

        public Task SaveAsync(SessionTokens tokens, CancellationToken cancellationToken)
        {
            if (Kept(_store.Load()) != null)
            {
                _store.Save(new CliConfig { Server = _server, Login = new SessionLogin { Email = _email, Tokens = tokens } });
            }

            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken)
        {
            if (Kept(_store.Load()) != null)
            {
                _store.Delete();
            }

            return Task.CompletedTask;
        }

        private SessionTokens? Kept(CliConfig? config)
        {
            return config?.Server == _server && config.Login is SessionLogin session && session.Email == _email ? session.Tokens : null;
        }
    }
}
