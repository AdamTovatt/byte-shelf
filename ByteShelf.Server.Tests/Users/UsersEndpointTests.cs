using System.Net;
using System.Net.Http.Json;
using ByteShelf.Server.Shared.Auth;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelf.Server.Users.Models;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.Users
{
    /// <summary>The users and password-reset endpoints as the pipeline serves them.</summary>
    public class UsersEndpointTests : IAsyncLifetime
    {
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
        public async Task GetMe_SignedIn_ReturnsThatUser()
        {
            User? me = await _host.MemberClient.GetFromJsonAsync<User>("/api/users/me");

            Assert.Equal(ServerTestHost.MemberEmail, me?.Email);
        }

        [Fact]
        public async Task PutMyPassword_WrongCurrentPassword_Returns401WithCode()
        {
            HttpResponseMessage response = await _host.MemberClient.PutAsJsonAsync(
                "/api/users/me/password", new { currentPassword = "wrong-password", newPassword = "new-password-1" });

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal(ErrorCodes.InvalidCredentials, (await response.Content.ReadFromJsonAsync<ErrorResponse>())?.Code);
        }

        [Fact]
        public async Task PostUser_WithoutAdmin_Returns403()
        {
            HttpResponseMessage response = await _host.MemberClient.PostAsJsonAsync(
                "/api/users", new { email = "new@example.com", displayName = "New", password = "password123" });

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task PutRoles_LastAdminDemoted_Returns409()
        {
            User me = (await _host.AdminClient.GetFromJsonAsync<User>("/api/users/me"))!;

            HttpResponseMessage response = await _host.AdminClient.PutAsJsonAsync($"/api/users/{me.Id}/roles", new { roles = Array.Empty<string>() });

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }

        [Fact]
        public async Task PostResetRequest_UnknownEmail_Returns204LikeAKnownOne()
        {
            HttpResponseMessage unknown = await _host.AnonymousClient.PostAsJsonAsync("/api/password-reset/request", new { email = "nobody@example.com" });
            HttpResponseMessage known = await _host.AnonymousClient.PostAsJsonAsync("/api/password-reset/request", new { email = ServerTestHost.MemberEmail });

            Assert.Equal(HttpStatusCode.NoContent, unknown.StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, known.StatusCode);
        }

        [Fact]
        public async Task PostResetComplete_InvalidToken_Returns400WithCode()
        {
            HttpResponseMessage response = await _host.AnonymousClient.PostAsJsonAsync(
                "/api/password-reset/complete", new { token = "not-a-token", newPassword = "new-password-1" });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(ErrorCodes.InvalidRequest, (await response.Content.ReadFromJsonAsync<ErrorResponse>())?.Code);
        }

        [Fact]
        public async Task GetUsers_Admin_ListsBothUsers()
        {
            IReadOnlyList<User>? users = await _host.AdminClient.GetFromJsonAsync<IReadOnlyList<User>>("/api/users");

            Assert.Equal(new[] { ServerTestHost.AdminEmail, ServerTestHost.MemberEmail }, users!.Select(user => user.Email).Order());
            Assert.Contains(users!, user => user.Roles.Contains(UserRoles.Admin));
        }
    }
}
