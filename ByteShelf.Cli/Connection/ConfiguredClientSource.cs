using System.Diagnostics;
using ByteShelf.Cli.Config;
using ByteShelf.Tools;
using ByteShelfClient;
using ByteShelfClient.Http;
using ByteShelfCommon;

namespace ByteShelf.Cli.Connection
{
    /// <summary>
    /// The client the environment or the config file describes. <see cref="ServerVariable"/> and
    /// <see cref="KeyVariable"/> win over the file, so a program or an agent can run commands with a
    /// key and no login. A kept session is shared with every other <c>byteshelf</c> process through
    /// <see cref="ConfigSessionStore"/>.
    /// </summary>
    internal sealed class ConfiguredClientSource : IClientSource, IDisposable
    {
        public const string ServerVariable = "BYTESHELF_URL";
        public const string KeyVariable = "BYTESHELF_KEY";

        private const string NotSignedIn =
            "Not signed in. Run 'byteshelf login <server-url> --email <email>' or 'byteshelf login <server-url> --key', " +
            "or set " + ServerVariable + " and " + KeyVariable + ".";

        private readonly CliConfigStore _store;
        private readonly Func<string, string?> _environment;
        private readonly HttpMessageHandler? _handler;
        private readonly TimeProvider _timeProvider;
        private readonly SemaphoreSlim _openLock = new SemaphoreSlim(1, 1);
        private readonly List<IDisposable> _owned = new List<IDisposable>();
        private OpenedCredential? _opened;

        /// <param name="store">Where the login is kept.</param>
        /// <param name="environment">Reads an environment variable.</param>
        /// <param name="handler">Sends the requests; a new socket handler when left out.</param>
        /// <param name="timeProvider">The clock a session's expiry is judged by.</param>
        public ConfiguredClientSource(CliConfigStore store, Func<string, string?> environment, HttpMessageHandler? handler = null, TimeProvider? timeProvider = null)
        {
            _store = store;
            _environment = environment;
            _handler = handler;
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        public async Task<IByteShelfClient> OpenAsync(CancellationToken cancellationToken)
        {
            Credential credential = Resolve() ?? throw new UsageException(NotSignedIn);
            await _openLock.WaitAsync(cancellationToken);
            try
            {
                // Compared by value: the same key, or the same user's session on the same server, is the
                // same client, however often the session's tokens have been renewed since.
                if (_opened?.Credential == credential)
                {
                    return _opened.Client;
                }

                HttpClient http = CreateHttp(credential.Server);
                IByteShelfClient client;
                switch (credential)
                {
                    case KeyCredential key:
                        client = new ByteShelfHttpClient(http, new ShelfKeyCredentials(key.Key));
                        break;
                    case SessionCredential session:
                        ConfigSessionStore sessionStore = new ConfigSessionStore(_store, session.Server, session.Email);
                        UserSession shared = await UserSession.OpenSharedAsync(http, sessionStore, _timeProvider, cancellationToken);
                        _owned.Add(shared);
                        client = new ByteShelfHttpClient(http, shared);
                        break;
                    default:
                        throw new UnreachableException();
                }

                _opened = new OpenedCredential(credential, client);
                return client;
            }
            finally
            {
                _openLock.Release();
            }
        }

        public async Task SignInAsync(string server, string email, string password, CancellationToken cancellationToken)
        {
            string address = NormalizeServer(server);
            using UserSession session = await UserSession.SignInAsync(CreateHttp(address), email, password, _timeProvider, cancellationToken);
            await using (await _store.LockAsync(cancellationToken))
            {
                _store.Save(new CliConfig { Server = address, Login = new SessionLogin { Email = email, Tokens = session.Tokens } });
            }
        }

        public async Task UseKeyAsync(string server, string key, CancellationToken cancellationToken)
        {
            string address = NormalizeServer(server);
            await new ByteShelfHttpClient(CreateHttp(address), new ShelfKeyCredentials(key)).GetShelvesAsync(cancellationToken);
            await using (await _store.LockAsync(cancellationToken))
            {
                _store.Save(new CliConfig { Server = address, Login = new KeyLogin { Key = key } });
            }
        }

        public async Task SignOutAsync(CancellationToken cancellationToken)
        {
            await using (await _store.LockAsync(cancellationToken))
            {
                CliConfig? config;
                try
                {
                    config = _store.Load();
                }
                catch (UsageException)
                {
                    // A damaged file keeps nothing to end on the server; removing it is the way out.
                    config = null;
                }

                if (config?.Login is SessionLogin kept)
                {
                    using UserSession session = UserSession.Resume(CreateHttp(config.Server), kept.Tokens, _timeProvider);
                    try
                    {
                        await session.SignOutAsync(cancellationToken);
                    }
                    catch (ByteShelfException)
                    {
                        // Already over on the server; forgetting it here is all that is left.
                    }
                    catch (HttpRequestException)
                    {
                        // The server cannot be reached; the session runs out on its own.
                    }
                }

                _store.Delete();
            }
        }

        public string? Describe()
        {
            return Resolve()?.Description;
        }

        public void Dispose()
        {
            foreach (IDisposable owned in _owned)
            {
                owned.Dispose();
            }

            _openLock.Dispose();
        }

        /// <summary>The server's address as an absolute http or https URL ending in <c>/</c>.</summary>
        /// <exception cref="UsageException">It is not one.</exception>
        public static string NormalizeServer(string server)
        {
            if (!Uri.TryCreate(server, UriKind.Absolute, out Uri? uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                throw new UsageException($"'{server}' is not a server address; give one like https://files.example.com/.");
            }

            string address = uri.GetLeftPart(UriPartial.Path);
            return address.EndsWith('/') ? address : address + "/";
        }

        /// <summary>
        /// The login in effect now, or null when there is none. Read afresh for every command and
        /// tool call, so a long-running MCP server follows a login or logout made elsewhere.
        /// </summary>
        /// <exception cref="UsageException">The environment names a server without a key, or the config file is damaged.</exception>
        private Credential? Resolve()
        {
            string? environmentServer = _environment(ServerVariable);
            string? environmentKey = _environment(KeyVariable);
            if (environmentServer != null)
            {
                // The file's credentials belong to the file's server, never to another one.
                string key = environmentKey ?? throw new UsageException($"{ServerVariable} is set, so {KeyVariable} must be too.");
                string server = NormalizeServer(environmentServer);
                return new KeyCredential(server, key, $"{server} with the key in {KeyVariable}");
            }

            CliConfig? config = _store.Load();
            if (config == null)
            {
                return null;
            }

            if (environmentKey != null)
            {
                return new KeyCredential(config.Server, environmentKey, $"{config.Server} with the key in {KeyVariable}");
            }

            return config.Login switch
            {
                KeyLogin key => new KeyCredential(config.Server, key.Key, $"{config.Server} with a shelf key"),
                SessionLogin session => new SessionCredential(config.Server, session.Email, $"{config.Server} as {session.Email}"),
                _ => throw new UnreachableException(),
            };
        }

        /// <summary>
        /// A client for <paramref name="server"/> without a timeout of its own: an upload or a download
        /// takes as long as it takes, and a command ends early only when it is cancelled.
        /// </summary>
        internal HttpClient CreateHttp(string server)
        {
            HttpClient http = _handler == null ? new HttpClient() : new HttpClient(_handler, disposeHandler: false);
            http.BaseAddress = new Uri(server);
            http.Timeout = Timeout.InfiniteTimeSpan;
            _owned.Add(http);
            return http;
        }

        /// <summary>A server and how to prove who you are there, with how <see cref="Describe"/> says so.</summary>
        private abstract record Credential(string Server, string Description);

        /// <summary>A shelf key.</summary>
        private sealed record KeyCredential(string Server, string Key, string Description) : Credential(Server, Description);

        /// <summary>The session the config file keeps for <paramref name="Email"/>; its tokens are read when it is used.</summary>
        private sealed record SessionCredential(string Server, string Email, string Description) : Credential(Server, Description);

        private sealed record OpenedCredential(Credential Credential, IByteShelfClient Client);
    }
}
