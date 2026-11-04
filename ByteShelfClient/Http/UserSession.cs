using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization.Metadata;
using ByteShelfCommon;
using ByteShelfCommon.Serialization;

namespace ByteShelfClient.Http
{
    /// <summary>
    /// A signed-in user's session with one server: a short-lived access token, renewed with the refresh
    /// token shortly before it runs out and again if the server refuses it. Each renewal replaces both;
    /// keep <see cref="Tokens"/> after <see cref="Renewed"/> to resume the session later, or give the
    /// session an <see cref="ISharedSessionStore"/> when several processes share it. The
    /// <see cref="HttpClient"/> is the caller's and is not disposed here.
    /// </summary>
    public sealed class UserSession : ByteShelfCredentials, IDisposable
    {
        /// <summary>How long before the access token runs out it is renewed.</summary>
        private static readonly TimeSpan RenewAhead = TimeSpan.FromMinutes(1);

        private readonly SemaphoreSlim _renewLock = new SemaphoreSlim(1, 1);
        private readonly HttpClient _http;
        private readonly TimeProvider _timeProvider;
        private readonly ISharedSessionStore? _store;
        private SessionTokens _tokens;
        private volatile bool _ended;

        private UserSession(HttpClient http, SessionTokens tokens, TimeProvider timeProvider, ISharedSessionStore? store)
        {
            _http = http;
            _tokens = tokens;
            _timeProvider = timeProvider;
            _store = store;
        }

        /// <summary>Raised after this session renewed its tokens, which <see cref="Tokens"/> then holds.</summary>
        public event EventHandler? Renewed;

        /// <summary>The session's current tokens, to keep and resume with <see cref="Resume"/>; they change with every renewal.</summary>
        public SessionTokens Tokens => _tokens;

        /// <summary>Signs a user in with their email and password.</summary>
        /// <param name="http">A client whose base address is the server's, ending in <c>/</c>; the session sends its renewals through it.</param>
        /// <param name="email">The user's email.</param>
        /// <param name="password">The user's password.</param>
        /// <param name="timeProvider">The clock the access token's expiry is judged by; the system's when left out.</param>
        /// <param name="cancellationToken">Cancels the sign-in.</param>
        /// <exception cref="ByteShelfException">The email or password is wrong (<see cref="ErrorCodes.Unauthenticated"/>).</exception>
        public static async Task<UserSession> SignInAsync(HttpClient http, string email, string password, TimeProvider? timeProvider = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(http);

            SessionTokens tokens = await PostForTokensAsync(
                http,
                "api/auth/login",
                new SignInRequest { Username = email, Password = password },
                ByteShelfJsonContext.Default.SignInRequest,
                cancellationToken).ConfigureAwait(false);

            return new UserSession(http, tokens, timeProvider ?? TimeProvider.System, store: null);
        }

        /// <summary>
        /// A session resumed from <see cref="Tokens"/> kept earlier. It uses the access token while it
        /// works and renews with the refresh token when it is about to run out or already has.
        /// </summary>
        /// <param name="http">A client whose base address is the server's, ending in <c>/</c>; the session sends its renewals through it.</param>
        /// <param name="tokens">The <see cref="Tokens"/> kept from the earlier session.</param>
        /// <param name="timeProvider">The clock the access token's expiry is judged by; the system's when left out.</param>
        public static UserSession Resume(HttpClient http, SessionTokens tokens, TimeProvider? timeProvider = null)
        {
            ArgumentNullException.ThrowIfNull(http);
            ArgumentNullException.ThrowIfNull(tokens);
            ArgumentException.ThrowIfNullOrWhiteSpace(tokens.RefreshToken, nameof(tokens));
            return new UserSession(http, tokens, timeProvider ?? TimeProvider.System, store: null);
        }

        /// <summary>
        /// The session kept in <paramref name="store"/>, shared with other processes: every renewal
        /// happens with the store locked, takes up tokens another process renewed to, and is saved there.
        /// </summary>
        /// <param name="http">A client whose base address is the server's, ending in <c>/</c>; the session sends its renewals through it.</param>
        /// <param name="store">Where the session is kept.</param>
        /// <param name="timeProvider">The clock the access token's expiry is judged by; the system's when left out.</param>
        /// <param name="cancellationToken">Cancels reading the store.</param>
        /// <exception cref="ByteShelfException">The store keeps no session (<see cref="ErrorCodes.Unauthenticated"/>).</exception>
        public static async Task<UserSession> OpenSharedAsync(HttpClient http, ISharedSessionStore store, TimeProvider? timeProvider = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(http);
            ArgumentNullException.ThrowIfNull(store);
            SessionTokens tokens = await store.LoadAsync(cancellationToken).ConfigureAwait(false) ?? throw NotKept();
            return new UserSession(http, tokens, timeProvider ?? TimeProvider.System, store);
        }

        /// <summary>
        /// Ends the session on the server, so its refresh token stops working, and here, so this session
        /// sends no more requests. A shared session is signed out with the store locked, with the tokens it
        /// keeps now, which another process may have renewed, and the store stops keeping it.
        /// </summary>
        /// <exception cref="ByteShelfException">The server refused; the session is left as it was.</exception>
        public async Task SignOutAsync(CancellationToken cancellationToken = default)
        {
            await _renewLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                IAsyncDisposable? storeLock = _store == null ? null : await _store.LockAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    SessionTokens? current = _store == null
                        ? _tokens
                        : await _store.LoadAsync(cancellationToken).ConfigureAwait(false);

                    // A store that keeps nothing has been signed out already, by another process.
                    if (current != null)
                    {
                        using HttpResponseMessage response = await _http.PostAsJsonAsync(
                            "api/auth/logout",
                            new RefreshTokenRequest { RefreshToken = current.RefreshToken },
                            ByteShelfJsonContext.Default.RefreshTokenRequest,
                            cancellationToken).ConfigureAwait(false);

                        await ApiErrors.ThrowIfFailedAsync(response, cancellationToken).ConfigureAwait(false);

                        if (_store != null)
                        {
                            await _store.ClearAsync(CancellationToken.None).ConfigureAwait(false);
                        }
                    }

                    _ended = true;
                }
                finally
                {
                    if (storeLock != null)
                    {
                        await storeLock.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                _renewLock.Release();
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _renewLock.Dispose();
        }

        internal override async Task ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_ended)
            {
                throw NotKept();
            }

            SessionTokens tokens = _tokens;
            if (IsDue(tokens))
            {
                tokens = await RenewAsync(tokens.Token, cancellationToken).ConfigureAwait(false);
            }

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.Token);
        }

        internal override async Task<bool> RenewAfterRejectionAsync(HttpRequestMessage rejected, CancellationToken cancellationToken)
        {
            await RenewAsync(rejected.Headers.Authorization?.Parameter ?? string.Empty, cancellationToken).ConfigureAwait(false);
            return true;
        }

        private static ByteShelfException NotKept()
        {
            return new ByteShelfException("The session is no longer kept; sign in again.", ErrorCodes.Unauthenticated);
        }

        /// <summary>Posts to a sign-in endpoint and reads the tokens it answers.</summary>
        private static async Task<SessionTokens> PostForTokensAsync<TBody>(HttpClient http, string uri, TBody body, JsonTypeInfo<TBody> bodyType, CancellationToken cancellationToken)
        {
            using HttpResponseMessage response = await http.PostAsJsonAsync(uri, body, bodyType, cancellationToken).ConfigureAwait(false);
            await ApiErrors.ThrowIfFailedAsync(response, cancellationToken).ConfigureAwait(false);
            return await ApiConnection.ReadAsync(response, ByteShelfJsonContext.Default.SessionTokens, cancellationToken).ConfigureAwait(false);
        }

        private bool IsDue(SessionTokens tokens)
        {
            return _timeProvider.GetUtcNow() + RenewAhead >= tokens.ExpiresAt;
        }

        /// <summary>
        /// Answers tokens newer than <paramref name="staleToken"/>: ones another request (or, with a
        /// store, another process) renewed to when they are not due themselves, else renewed ones.
        /// One renewal at a time, since a refresh token works once.
        /// </summary>
        /// <exception cref="ByteShelfException">
        /// The server refused: <see cref="ErrorCodes.Unauthenticated"/> when the session has ended, or the
        /// server's own error, which leaves the session as it was.
        /// </exception>
        private async Task<SessionTokens> RenewAsync(string staleToken, CancellationToken cancellationToken)
        {
            SessionTokens renewed;
            await _renewLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_ended)
                {
                    throw NotKept();
                }

                IAsyncDisposable? storeLock = _store == null ? null : await _store.LockAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    SessionTokens current = _store == null
                        ? _tokens
                        : await _store.LoadAsync(cancellationToken).ConfigureAwait(false) ?? throw NotKept();

                    if (current.Token != staleToken && !IsDue(current))
                    {
                        _tokens = current;
                        return current;
                    }

                    // Not cancelled with the caller's request: once the server has rotated the refresh
                    // token, abandoning its answer would leave the session holding the spent one, and
                    // sending that again ends every session in its family.
                    renewed = await PostForTokensAsync(
                        _http,
                        "api/auth/refresh",
                        new RefreshTokenRequest { RefreshToken = current.RefreshToken },
                        ByteShelfJsonContext.Default.RefreshTokenRequest,
                        CancellationToken.None).ConfigureAwait(false);

                    _tokens = renewed;
                    if (_store != null)
                    {
                        await _store.SaveAsync(renewed, CancellationToken.None).ConfigureAwait(false);
                    }
                }
                finally
                {
                    if (storeLock != null)
                    {
                        await storeLock.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                _renewLock.Release();
            }

            Renewed?.Invoke(this, EventArgs.Empty);
            return renewed;
        }
    }
}
