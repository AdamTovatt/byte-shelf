using System.Net;
using System.Net.Http.Json;
using ByteShelf.Server.Shared.RateLimiting;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace ByteShelf.Server.Tests.Shared
{
    /// <summary>
    /// The rate-limit tiers as the pipeline applies them. Each request is one the endpoint refuses before
    /// any real work (a body it cannot use), and never a 401, which the sign-in delay would slow down.
    /// </summary>
    public sealed class RateLimitingTests : IAsyncLifetime
    {
        private static readonly IPAddress Caller = IPAddress.Parse("192.0.2.1");
        private static readonly IPAddress OtherCaller = IPAddress.Parse("192.0.2.2");

        private ServerTestHost _host = null!;

        public async Task InitializeAsync()
        {
            _host = await ServerTestHost.StartAsync();
        }

        public async Task DisposeAsync()
        {
            await _host.DisposeAsync();
        }

        [Fact]
        public async Task SignInAndRenewal_PastTheStrictTierTogether_Answer429()
        {
            using HttpClient client = CreateClientFrom(Caller);

            for (int request = 0; request < RateLimitPolicies.StrictPermitsPerMinute; request++)
            {
                string path = request % 2 == 0 ? "/api/auth/login" : "/api/auth/refresh";
                Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(path, new { })).StatusCode);
            }

            await AssertRefusedAsync(await client.PostAsJsonAsync("/api/auth/login", new { }));
            await AssertRefusedAsync(await client.PostAsJsonAsync("/api/auth/refresh", new { }));
        }

        [Fact]
        public async Task SignIn_OneCallerPastTheStrictTier_LeavesAnotherCallerAlone()
        {
            using HttpClient client = CreateClientFrom(Caller);
            using HttpClient other = CreateClientFrom(OtherCaller);
            for (int request = 0; request < RateLimitPolicies.StrictPermitsPerMinute; request++)
            {
                await client.PostAsJsonAsync("/api/auth/login", new { });
            }

            HttpResponseMessage response = await other.PostAsJsonAsync("/api/auth/login", new { });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task StrictRoute_PastTheTier_Answers429ForThatUserOnly()
        {
            for (int request = 0; request < RateLimitPolicies.StrictPermitsPerMinute; request++)
            {
                Assert.Equal(HttpStatusCode.BadRequest, (await _host.MemberClient.PutAsJsonAsync("/api/users/me/password", new { })).StatusCode);
            }

            await AssertRefusedAsync(await _host.MemberClient.PutAsJsonAsync("/api/users/me/password", new { }));

            // Both clients reach the in-memory server from the same (missing) address: the tier counts users.
            Assert.Equal(HttpStatusCode.BadRequest, (await _host.AdminClient.PutAsJsonAsync("/api/users/me/password", new { })).StatusCode);
        }

        [Fact]
        public async Task PairingAccept_PastTheVeryStrictTier_Answers429()
        {
            using HttpClient client = CreateClientFrom(Caller);

            for (int request = 0; request < RateLimitPolicies.VeryStrictPermitsPerMinute; request++)
            {
                Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/replication/feed/accept", new { })).StatusCode);
            }

            await AssertRefusedAsync(await client.PostAsJsonAsync("/api/replication/feed/accept", new { }));
        }

        [Fact]
        public async Task PasswordResetRequest_PastTheVeryStrictTier_Answers429()
        {
            using HttpClient client = CreateClientFrom(Caller);

            for (int request = 0; request < RateLimitPolicies.VeryStrictPermitsPerMinute; request++)
            {
                Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/password-reset/request", new { email = "nobody@example.com" })).StatusCode);
            }

            await AssertRefusedAsync(await client.PostAsJsonAsync("/api/password-reset/request", new { email = "nobody@example.com" }));
        }

        [Fact]
        public async Task AnyRoute_PastTheGlobalTier_Answers429()
        {
            using HttpClient client = CreateClientFrom(Caller);

            for (int request = 0; request < RateLimitPolicies.GlobalPermitsPerMinute; request++)
            {
                Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/version/current")).StatusCode);
            }

            await AssertRefusedAsync(await client.GetAsync("/api/version/current"));
        }

        private static async Task AssertRefusedAsync(HttpResponseMessage response)
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
            Assert.False(string.IsNullOrEmpty((await response.Content.ReadFromJsonAsync<ErrorResponse>())?.Message));
        }

        /// <summary>A client whose requests reach the server from <paramref name="address"/>, which the in-memory server leaves unset otherwise.</summary>
        private HttpClient CreateClientFrom(IPAddress address)
        {
            TestServer server = (TestServer)_host.Services.GetRequiredService<IServer>();
            return new HttpClient(server.CreateHandler(context => context.Connection.RemoteIpAddress = address)) { BaseAddress = server.BaseAddress };
        }
    }
}
