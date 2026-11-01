using System.Net;
using System.Net.Http.Json;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.Replication
{
    /// <summary>Sessions on a replica after the password they were signed in with changes on the primary.</summary>
    public sealed class ReplicaSessionTests : IAsyncLifetime
    {
        private ReplicationPair _pair = null!;

        public async Task InitializeAsync()
        {
            _pair = await ReplicationPair.StartAsync();
            await _pair.PairAsync();
            await _pair.SyncAsync();
        }

        public async Task DisposeAsync()
        {
            await _pair.DisposeAsync();
        }

        [Fact]
        public async Task PasswordChangedOnThePrimary_EndsTheSessionOnTheReplica()
        {
            string refreshToken = await SignInOnReplicaAsync();

            HttpResponseMessage changed = await _pair.Primary.AdminClient.PutAsJsonAsync(
                "/api/users/me/password", new { currentPassword = ServerTestHost.Password, newPassword = "a-new-password" });
            changed.EnsureSuccessStatusCode();
            await _pair.SyncAsync();

            Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshOnReplicaAsync(refreshToken)).StatusCode);
        }

        [Fact]
        public async Task NothingChangedOnThePrimary_TheSessionOnTheReplicaRenews()
        {
            string refreshToken = await SignInOnReplicaAsync();

            await _pair.CreateShelfAsync("docs");
            await _pair.SyncAsync();

            Assert.Equal(HttpStatusCode.OK, (await RefreshOnReplicaAsync(refreshToken)).StatusCode);
        }

        private async Task<string> SignInOnReplicaAsync()
        {
            HttpResponseMessage login = await _pair.Replica.AnonymousClient.PostAsJsonAsync(
                "/api/auth/login", new { username = ServerTestHost.AdminEmail, password = ServerTestHost.Password });
            login.EnsureSuccessStatusCode();
            return (await login.Content.ReadFromJsonAsync<SessionTokens>())!.RefreshToken;
        }

        private Task<HttpResponseMessage> RefreshOnReplicaAsync(string refreshToken)
        {
            return _pair.Replica.AnonymousClient.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest { RefreshToken = refreshToken });
        }
    }
}
