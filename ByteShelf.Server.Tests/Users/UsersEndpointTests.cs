using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ByteShelf.Server.Shared.Auth;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelf.Server.Users.Repositories;
using ByteShelfCommon;
using EasyReasy.Auth;
using Microsoft.Extensions.DependencyInjection;

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
            UserInfo? me = await _host.MemberClient.GetFromJsonAsync<UserInfo>("/api/users/me");

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
            UserInfo me = (await _host.AdminClient.GetFromJsonAsync<UserInfo>("/api/users/me"))!;

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
            IReadOnlyList<UserInfo>? users = await _host.AdminClient.GetFromJsonAsync<IReadOnlyList<UserInfo>>("/api/users");

            Assert.Equal(new[] { ServerTestHost.AdminEmail, ServerTestHost.MemberEmail }, users!.Select(user => user.Email).Order());
            Assert.Contains(users!, user => user.Roles.Contains(UserRoles.Admin));
        }

        [Fact]
        public async Task DemotedAdmin_TheirStillValidToken_LosesAdminAtOnce()
        {
            (UserInfo second, HttpClient secondClient) = await CreateSecondAdminAsync();
            (await _host.AdminClient.PutAsJsonAsync($"/api/users/{second.Id}/roles", new { roles = Array.Empty<string>() })).EnsureSuccessStatusCode();

            HttpResponseMessage list = await secondClient.GetAsync("/api/users");
            HttpResponseMessage create = await secondClient.PostAsJsonAsync(
                "/api/users", new { email = "new@example.com", displayName = "New", password = "password123", roles = new[] { UserRoles.Admin } });

            Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        }

        [Fact]
        public async Task DeletedAdmin_TheirStillValidToken_IsRefusedAtOnce()
        {
            (UserInfo second, HttpClient secondClient) = await CreateSecondAdminAsync();
            (await _host.AdminClient.DeleteAsync($"/api/users/{second.Id}")).EnsureSuccessStatusCode();

            HttpResponseMessage create = await secondClient.PostAsJsonAsync(
                "/api/users", new { email = "new@example.com", displayName = "New", password = "password123", roles = new[] { UserRoles.Admin } });
            HttpResponseMessage shelves = await secondClient.GetAsync("/api/shelves");

            Assert.Equal(HttpStatusCode.Unauthorized, create.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, shelves.StatusCode);
        }

        [Fact]
        public async Task PromotedMember_TheirStillValidToken_GainsAdminAtOnce()
        {
            (await _host.AdminClient.PutAsJsonAsync($"/api/users/{_host.MemberId}/roles", new { roles = new[] { UserRoles.Admin } })).EnsureSuccessStatusCode();

            HttpResponseMessage response = await _host.MemberClient.GetAsync("/api/users");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task PutMyPassword_EndsTheOtherSessionsButNotThisOne()
        {
            (HttpClient changing, string changingRefresh) = await SignInAsync(ServerTestHost.MemberEmail, ServerTestHost.Password);
            (_, string otherRefresh) = await SignInAsync(ServerTestHost.MemberEmail, ServerTestHost.Password);

            (await changing.PutAsJsonAsync("/api/users/me/password", new { currentPassword = ServerTestHost.Password, newPassword = "new-password-1" })).EnsureSuccessStatusCode();

            Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(changingRefresh)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(otherRefresh)).StatusCode);
        }

        [Fact]
        public async Task PostResetComplete_EndsEverySession()
        {
            (_, string refresh) = await SignInAsync(ServerTestHost.MemberEmail, ServerTestHost.Password);
            string token = await RequestResetTokenAsync(ServerTestHost.MemberEmail);

            (await _host.AnonymousClient.PostAsJsonAsync("/api/password-reset/complete", new { token, newPassword = "new-password-1" })).EnsureSuccessStatusCode();

            Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(refresh)).StatusCode);
        }

        private async Task<(UserInfo User, HttpClient Client)> CreateSecondAdminAsync()
        {
            HttpResponseMessage created = await _host.AdminClient.PostAsJsonAsync(
                "/api/users", new { email = "second@byteshelf.test", displayName = "Second", password = ServerTestHost.Password, roles = new[] { UserRoles.Admin } });
            created.EnsureSuccessStatusCode();
            UserInfo second = (await created.Content.ReadFromJsonAsync<UserInfo>())!;

            HttpClient client = await _host.CreateSignedInClientAsync(second.Email);
            (await client.GetAsync("/api/users")).EnsureSuccessStatusCode();
            return (second, client);
        }

        private async Task<(HttpClient Client, string RefreshToken)> SignInAsync(string email, string password)
        {
            HttpResponseMessage login = await _host.AnonymousClient.PostAsJsonAsync("/api/auth/login", new { username = email, password });
            login.EnsureSuccessStatusCode();
            SessionTokens tokens = (await login.Content.ReadFromJsonAsync<SessionTokens>())!;

            HttpClient client = _host.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.Token);
            return (client, tokens.RefreshToken);
        }

        private Task<HttpResponseMessage> RefreshAsync(string refreshToken)
        {
            return _host.AnonymousClient.PostAsJsonAsync("/api/auth/refresh", new { refreshToken });
        }

        /// <summary>A reset token for <paramref name="email"/>, stored the way a request stores one.</summary>
        private async Task<string> RequestResetTokenAsync(string email)
        {
            using IServiceScope scope = _host.Services.CreateScope();
            Guid userId = (await scope.ServiceProvider.GetRequiredService<IUserLoginRepository>().GetCredentialsByEmailAsync(email))!.User.Id;
            PasswordResetToken token = scope.ServiceProvider.GetRequiredService<IPasswordResetTokenHandler>().GenerateResetToken();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            await scope.ServiceProvider.GetRequiredService<IPasswordResetTokenRepository>().CreateAsync(userId, token.TokenHash, now, now.AddHours(1));
            return token.Token;
        }
    }
}
