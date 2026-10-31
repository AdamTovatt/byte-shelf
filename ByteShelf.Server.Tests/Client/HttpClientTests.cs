using System.Text;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfClient;
using ByteShelfClient.Http;
using ByteShelfCommon;
using Microsoft.Extensions.Time.Testing;

namespace ByteShelf.Server.Tests.Client
{
    /// <summary>What only the HTTP client does, against the whole server: keys, signing in, and keeping a session alive.</summary>
    public class HttpClientTests : IAsyncLifetime
    {
        private ServerTestHost _host = null!;
        private HttpClient _http = null!;

        public async Task InitializeAsync()
        {
            _host = await ServerTestHost.StartAsync();
            _http = _host.CreateClient();
        }

        public async Task DisposeAsync()
        {
            _http.Dispose();
            await _host.DisposeAsync();
        }

        [Fact]
        public async Task ShelfKeyCredentials_SeeOnlyTheirOwnShelf()
        {
            IByteShelfClient admin = await SignInAdminAsync();
            await admin.CreateShelfAsync("docs");
            await admin.CreateShelfAsync("other");
            CreatedShelfKey key = await admin.Shelf("docs").CreateKeyAsync("app", ShelfKeyPermission.ReadWrite);

            ByteShelfHttpClient program = new ByteShelfHttpClient(_http, new ShelfKeyCredentials(key.Secret));

            Assert.Equal("docs", Assert.Single(await program.GetShelvesAsync()).Name);
            Assert.Equal(ErrorCodes.NotFound, (await Assert.ThrowsAsync<ByteShelfException>(() => program.Shelf("other").GetInfoAsync())).Code);
        }

        [Fact]
        public async Task ShelfKeyCredentials_AddOnlyKeyOverwriting_ThrowsPermissionDenied()
        {
            IByteShelfClient admin = await SignInAdminAsync();
            await admin.CreateShelfAsync("docs");
            await admin.Shelf("docs").UploadAsync("/a.txt", new MemoryStream(Encoding.UTF8.GetBytes("old")));
            CreatedShelfKey key = await admin.Shelf("docs").CreateKeyAsync("app", ShelfKeyPermission.AddOnly);
            IShelfClient program = new ByteShelfHttpClient(_http, new ShelfKeyCredentials(key.Secret)).Shelf("docs");

            ByteShelfException exception = await Assert.ThrowsAsync<ByteShelfException>(
                () => program.UploadAsync("/a.txt", new MemoryStream(Encoding.UTF8.GetBytes("new")), new UploadOptions { Overwrite = true }));

            Assert.Equal(ErrorCodes.PermissionDenied, exception.Code);
        }

        [Fact]
        public async Task SignInAsync_WrongPassword_ThrowsUnauthenticated()
        {
            ByteShelfException exception = await Assert.ThrowsAsync<ByteShelfException>(
                () => UserSession.SignInAsync(_http, ServerTestHost.AdminEmail, "not-the-password"));

            Assert.Equal(ErrorCodes.Unauthenticated, exception.Code);
        }

        [Fact]
        public async Task UserSession_TokenPastItsExpiry_IsRenewedBeforeTheRequest()
        {
            FakeTimeProvider clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
            using UserSession session = await UserSession.SignInAsync(_http, ServerTestHost.AdminEmail, ServerTestHost.Password, clock);
            string firstRefreshToken = session.Tokens.RefreshToken;
            int renewals = 0;
            session.Renewed += (_, _) => renewals++;
            clock.Advance(TimeSpan.FromHours(2));

            UserInfo me = await new ByteShelfHttpClient(_http, session).GetCurrentUserAsync();

            Assert.Equal(ServerTestHost.AdminEmail, me.Email);
            Assert.Equal(1, renewals);
            Assert.NotEqual(firstRefreshToken, session.Tokens.RefreshToken);
        }

        [Fact]
        public async Task UserSession_ResumedAfterItsAccessTokenRanOut_RenewsAndSignsTheUserBackIn()
        {
            SessionTokens kept;
            using (UserSession first = await UserSession.SignInAsync(_http, ServerTestHost.AdminEmail, ServerTestHost.Password))
            {
                kept = first.Tokens;
            }

            using UserSession resumed = UserSession.Resume(_http, RunOut(kept));

            Assert.Equal(ServerTestHost.AdminEmail, (await new ByteShelfHttpClient(_http, resumed).GetCurrentUserAsync()).Email);
            Assert.NotEqual(kept.RefreshToken, resumed.Tokens.RefreshToken);
        }

        [Fact]
        public async Task UserSession_SignedOut_CannotBeResumed()
        {
            using UserSession session = await UserSession.SignInAsync(_http, ServerTestHost.AdminEmail, ServerTestHost.Password);
            await session.SignOutAsync();

            using UserSession resumed = UserSession.Resume(_http, RunOut(session.Tokens));

            ByteShelfException exception = await Assert.ThrowsAsync<ByteShelfException>(() => new ByteShelfHttpClient(_http, resumed).GetCurrentUserAsync());
            Assert.Equal(ErrorCodes.Unauthenticated, exception.Code);
        }

        /// <summary>The tokens as if the access token had run out, so the next request renews; signing out does not revoke an access token.</summary>
        private static SessionTokens RunOut(SessionTokens tokens)
        {
            return new SessionTokens { Token = tokens.Token, ExpiresAt = DateTimeOffset.MinValue, RefreshToken = tokens.RefreshToken };
        }

        private async Task<IByteShelfClient> SignInAdminAsync()
        {
            UserSession session = await UserSession.SignInAsync(_http, ServerTestHost.AdminEmail, ServerTestHost.Password);
            return new ByteShelfHttpClient(_http, session);
        }
    }
}
