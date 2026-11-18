using System.Net;
using System.Net.Http.Json;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelf.Server.Users.Models;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.Replication
{
    /// <summary>Passkeys on a replica: made on the primary, signing in on either with a sign count of each server's own.</summary>
    public sealed class ReplicaPasskeyTests : IAsyncLifetime, IDisposable
    {
        private readonly SoftwareAuthenticator _authenticator = new SoftwareAuthenticator();
        private ReplicationPair _pair = null!;
        private PasskeyInfo _passkey = null!;

        public async Task InitializeAsync()
        {
            _pair = await ReplicationPair.StartAsync(withPasskeys: true);
            _passkey = await PasskeyCeremonies.AddAsync(_pair.Primary.MemberClient, _authenticator);
            await _pair.PairAsync();
            await _pair.SyncAsync();
        }

        public async Task DisposeAsync()
        {
            await _pair.DisposeAsync();
        }

        public void Dispose()
        {
            _authenticator.Dispose();
        }

        [Fact]
        public async Task SignIn_OnTheReplica_CountsOnlyThere()
        {
            _authenticator.SignCount = 5;
            SessionTokens onReplica = await PasskeyCeremonies.SignInAsync(_pair.Replica.AnonymousClient, _authenticator);
            await _pair.SyncAsync();

            using HttpClient session = _pair.Replica.CreateClient(onReplica.Token);
            Assert.Equal(ServerTestHost.MemberEmail, (await session.GetFromJsonAsync<UserInfo>("/api/users/me"))?.Email);

            // The primary never heard of the count of 5, so a lower one still signs in there.
            _authenticator.SignCount = 3;
            Assert.Equal(HttpStatusCode.OK, (await PasskeyCeremonies.TrySignInAsync(_pair.Primary.AnonymousClient, _authenticator)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await PasskeyCeremonies.TrySignInAsync(_pair.Replica.AnonymousClient, _authenticator)).StatusCode);
        }

        [Fact]
        public async Task RemovedOnThePrimary_EndsTheSessionOnTheReplica()
        {
            SessionTokens onReplica = await PasskeyCeremonies.SignInAsync(_pair.Replica.AnonymousClient, _authenticator);

            (await _pair.Primary.MemberClient.DeleteAsync($"/api/users/me/passkeys/{_passkey.Id}")).EnsureSuccessStatusCode();
            await _pair.SyncAsync();

            using HttpClient session = _pair.Replica.CreateClient(onReplica.Token);
            Assert.Equal(HttpStatusCode.Unauthorized, (await session.GetAsync("/api/users/me")).StatusCode);
            HttpResponseMessage refreshed = await _pair.Replica.AnonymousClient.PostAsJsonAsync(
                "/api/auth/refresh", new RefreshTokenRequest { RefreshToken = onReplica.RefreshToken });
            Assert.Equal(HttpStatusCode.Unauthorized, refreshed.StatusCode);
        }

        [Fact]
        public async Task AddingAPasskey_OnTheReplica_IsRefused()
        {
            using HttpClient member = await _pair.Replica.CreateSignedInClientAsync(ServerTestHost.MemberEmail);

            HttpResponseMessage response = await PasskeyCeremonies.TryBeginAddingAsync(member);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal(ErrorCodes.ReadOnlyReplica, (await response.Content.ReadFromJsonAsync<ErrorResponse>())?.Code);
        }

        [Fact]
        public async Task List_OnTheReplica_OffersSignInButNotAdding()
        {
            using HttpClient member = await _pair.Replica.CreateSignedInClientAsync(ServerTestHost.MemberEmail);

            PasskeyList list = (await member.GetFromJsonAsync<PasskeyList>("/api/users/me/passkeys"))!;

            Assert.Equal((true, false), (list.Available, list.CanAdd));
            Assert.Equal(_passkey.Id, list.Passkeys.Single().Id);
        }
    }
}
