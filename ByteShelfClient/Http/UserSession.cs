using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization.Metadata;
using ByteShelfCommon;
using ByteShelfCommon.Serialization;

namespace ByteShelfClient.Http
{
    /// <summary>
    /// A signed-in user's session with one server: a short-lived access token, renewed with the refresh
    /// token shortly before it runs out and again if the server refuses it. Each renewal replaces the
    /// refresh token; keep <see cref="RefreshToken"/> after <see cref="Renewed"/> to resume the session
    /// later. The <see cref="HttpClient"/> is the caller's and is not disposed here.
    /// </summary>
    public sealed class UserSession : ByteShelfCredentials, IDisposable
    {
        /// <summary>How long before the access token runs out it is renewed.</summary>
        private static readonly TimeSpan RenewAhead = TimeSpan.FromMinutes(1);

        private readonly SemaphoreSlim _renewLock = new SemaphoreSlim(1, 1);
        private readonly HttpClient _http;
        private readonly TimeProvider _timeProvider;
        private SessionTokens _tokens;

        private UserSession(HttpClient http, SessionTokens tokens, TimeProvider timeProvider)
        {
            _http = http;
            _tokens = tokens;
            _timeProvider = timeProvider;
        }

        /// <summary>Raised after the session was renewed, with a new <see cref="RefreshToken"/>.</summary>
        public event EventHandler? Renewed;

        /// <summary>The token that renews the session; it changes with every renewal.</summary>
        public string RefreshToken => _tokens.RefreshToken;

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

            return new UserSession(http, tokens, timeProvider ?? TimeProvider.System);
        }

        /// <summary>A session resumed from a refresh token kept earlier; it is renewed before its first request.</summary>
        /// <param name="http">A client whose base address is the server's, ending in <c>/</c>; the session sends its renewals through it.</param>
        /// <param name="refreshToken">The <see cref="RefreshToken"/> kept from the earlier session.</param>
        /// <param name="timeProvider">The clock the access token's expiry is judged by; the system's when left out.</param>
        public static UserSession Resume(HttpClient http, string refreshToken, TimeProvider? timeProvider = null)
        {
            ArgumentNullException.ThrowIfNull(http);
            ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);
            SessionTokens tokens = new SessionTokens { Token = string.Empty, ExpiresAt = DateTimeOffset.MinValue, RefreshToken = refreshToken };
            return new UserSession(http, tokens, timeProvider ?? TimeProvider.System);
        }

        /// <summary>Ends the session on the server, so its refresh token stops working.</summary>
        public async Task SignOutAsync(CancellationToken cancellationToken = default)
        {
            using HttpResponseMessage response = await _http.PostAsJsonAsync(
                "api/auth/logout",
                new RefreshTokenRequest { RefreshToken = _tokens.RefreshToken },
                ByteShelfJsonContext.Default.RefreshTokenRequest,
                cancellationToken).ConfigureAwait(false);

            await ApiErrors.ThrowIfFailedAsync(response, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _renewLock.Dispose();
        }

        internal override async Task ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            SessionTokens tokens = _tokens;
            if (_timeProvider.GetUtcNow() + RenewAhead >= tokens.ExpiresAt)
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

        /// <summary>Posts to a sign-in endpoint and reads the tokens it answers.</summary>
        private static async Task<SessionTokens> PostForTokensAsync<TBody>(HttpClient http, string uri, TBody body, JsonTypeInfo<TBody> bodyType, CancellationToken cancellationToken)
        {
            using HttpResponseMessage response = await http.PostAsJsonAsync(uri, body, bodyType, cancellationToken).ConfigureAwait(false);
            await ApiErrors.ThrowIfFailedAsync(response, cancellationToken).ConfigureAwait(false);
            return await ApiConnection.ReadAsync(response, ByteShelfJsonContext.Default.SessionTokens, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Renews the session unless another request already replaced <paramref name="staleToken"/>, and
        /// answers the current tokens. One renewal at a time, since a refresh token works once.
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
                if (_tokens.Token != staleToken)
                {
                    return _tokens;
                }

                // Not cancelled with the caller's request: once the server has rotated the refresh token,
                // abandoning its answer would leave the session holding the spent one, and sending that
                // again ends every session in its family.
                renewed = await PostForTokensAsync(
                    _http,
                    "api/auth/refresh",
                    new RefreshTokenRequest { RefreshToken = _tokens.RefreshToken },
                    ByteShelfJsonContext.Default.RefreshTokenRequest,
                    CancellationToken.None).ConfigureAwait(false);

                _tokens = renewed;
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
