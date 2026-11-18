using System.Buffers.Text;
using System.Net;
using System.Net.Http.Json;
using ByteShelf.Server.ChangeLog.Models;
using ByteShelf.Server.ChangeLog.Repositories;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelf.Server.Users.Models;
using ByteShelf.Server.Users.Services;
using ByteShelfCommon;
using EasyReasy.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace ByteShelf.Server.Tests.Users
{
    /// <summary>
    /// The passkey endpoints as the pipeline serves them, a <see cref="SoftwareAuthenticator"/> standing in
    /// for the browser and the real verifier checking it.
    /// </summary>
    public sealed class PasskeyEndpointTests : IAsyncLifetime, IDisposable
    {
        private readonly SoftwareAuthenticator _authenticator = new SoftwareAuthenticator();
        private ServerTestHost _host = null!;

        public async Task InitializeAsync()
        {
            _host = await ServerTestHost.StartWithPasskeysAsync();
        }

        public async Task DisposeAsync()
        {
            await _host.DisposeAsync();
        }

        public void Dispose()
        {
            _authenticator.Dispose();
        }

        [Fact]
        public async Task SignIn_WithAnAddedPasskey_SignsItsOwnerIn()
        {
            await PasskeyCeremonies.AddAsync(_host.MemberClient, _authenticator);

            SessionTokens tokens = await PasskeyCeremonies.SignInAsync(_host.AnonymousClient, _authenticator);

            using HttpClient session = _host.CreateClient(tokens.Token);
            Assert.Equal(ServerTestHost.MemberEmail, (await session.GetFromJsonAsync<UserInfo>("/api/users/me"))?.Email);
            Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(tokens.RefreshToken)).StatusCode);
        }

        [Fact]
        public async Task SignIn_TwoUsersWithPasskeys_SignsInTheOwnerOfThePasskeyUsed()
        {
            // The passkey is looked up by its credential id: the admin's, added first, is not the one used.
            using SoftwareAuthenticator adminAuthenticator = new SoftwareAuthenticator();
            await PasskeyCeremonies.AddAsync(_host.AdminClient, adminAuthenticator);
            await PasskeyCeremonies.AddAsync(_host.MemberClient, _authenticator);

            SessionTokens tokens = await PasskeyCeremonies.SignInAsync(_host.AnonymousClient, _authenticator);

            using HttpClient session = _host.CreateClient(tokens.Token);
            Assert.Equal(ServerTestHost.MemberEmail, (await session.GetFromJsonAsync<UserInfo>("/api/users/me"))?.Email);
        }

        [Fact]
        public async Task SignIn_UnknownPasskey_Returns401()
        {
            HttpResponseMessage response = await PasskeyCeremonies.TrySignInAsync(_host.AnonymousClient, _authenticator);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal(ErrorCodes.InvalidCredentials, (await response.Content.ReadFromJsonAsync<ErrorResponse>())?.Code);
        }

        [Fact]
        public async Task SignIn_UserHandleOfAnotherUser_Returns401()
        {
            await PasskeyCeremonies.AddAsync(_host.MemberClient, _authenticator);
            UserInfo admin = (await _host.AdminClient.GetFromJsonAsync<UserInfo>("/api/users/me"))!;
            _authenticator.UserHandle = admin.Id.ToByteArray();

            HttpResponseMessage response = await PasskeyCeremonies.TrySignInAsync(_host.AnonymousClient, _authenticator);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task SignIn_UserNotVerified_Returns401()
        {
            await PasskeyCeremonies.AddAsync(_host.MemberClient, _authenticator);
            _authenticator.VerifiesUser = false;

            HttpResponseMessage response = await PasskeyCeremonies.TrySignInAsync(_host.AnonymousClient, _authenticator);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task SignIn_CeremonyAnsweredTwice_RefusesTheSecond()
        {
            await PasskeyCeremonies.AddAsync(_host.MemberClient, _authenticator);
            PasskeyCeremony ceremony = await PasskeyCeremonies.BeginSignInAsync(_host.AnonymousClient);
            _authenticator.SignCount = 1;
            (await PasskeyCeremonies.TrySignInAsync(_host.AnonymousClient, ceremony, _authenticator)).EnsureSuccessStatusCode();

            _authenticator.SignCount = 2;
            HttpResponseMessage again = await PasskeyCeremonies.TrySignInAsync(_host.AnonymousClient, ceremony, _authenticator);

            Assert.Equal(HttpStatusCode.Unauthorized, again.StatusCode);
        }

        [Fact]
        public async Task SignIn_CeremonyFailedOnce_CannotBeAnsweredAgain()
        {
            await PasskeyCeremonies.AddAsync(_host.MemberClient, _authenticator);
            PasskeyCeremony ceremony = await PasskeyCeremonies.BeginSignInAsync(_host.AnonymousClient);
            _authenticator.VerifiesUser = false;
            Assert.Equal(HttpStatusCode.Unauthorized, (await PasskeyCeremonies.TrySignInAsync(_host.AnonymousClient, ceremony, _authenticator)).StatusCode);

            _authenticator.VerifiesUser = true;
            HttpResponseMessage again = await PasskeyCeremonies.TrySignInAsync(_host.AnonymousClient, ceremony, _authenticator);

            Assert.Equal(HttpStatusCode.Unauthorized, again.StatusCode);
        }

        [Fact]
        public async Task SignIn_CounterBelowTheLastSignIn_Returns401()
        {
            await PasskeyCeremonies.AddAsync(_host.MemberClient, _authenticator);
            _authenticator.SignCount = 5;
            await PasskeyCeremonies.SignInAsync(_host.AnonymousClient, _authenticator);

            _authenticator.SignCount = 3;
            HttpResponseMessage cloned = await PasskeyCeremonies.TrySignInAsync(_host.AnonymousClient, _authenticator);

            Assert.Equal(HttpStatusCode.Unauthorized, cloned.StatusCode);
        }

        [Fact]
        public async Task SignIn_CounterAboveTheLastSignIn_SignsIn()
        {
            await PasskeyCeremonies.AddAsync(_host.MemberClient, _authenticator);
            _authenticator.SignCount = 5;
            await PasskeyCeremonies.SignInAsync(_host.AnonymousClient, _authenticator);

            _authenticator.SignCount = 6;
            HttpResponseMessage response = await PasskeyCeremonies.TrySignInAsync(_host.AnonymousClient, _authenticator);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task Add_UserNotVerified_Returns400AndAddsNothing()
        {
            _authenticator.VerifiesUser = false;

            HttpResponseMessage response = await PasskeyCeremonies.TryAddAsync(
                _host.MemberClient, await PasskeyCeremonies.BeginAddingAsync(_host.MemberClient), _authenticator);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Empty((await ListAsync(_host.MemberClient)).Passkeys);
        }

        [Fact]
        public async Task Add_CeremonyBegunByAnotherUser_Returns400()
        {
            PasskeyCeremony adminsCeremony = await PasskeyCeremonies.BeginAddingAsync(_host.AdminClient);

            HttpResponseMessage response = await PasskeyCeremonies.TryAddAsync(_host.MemberClient, adminsCeremony, _authenticator);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Empty((await ListAsync(_host.MemberClient)).Passkeys);
        }

        [Fact]
        public async Task Add_CeremonyAnsweredTwice_RefusesTheSecond()
        {
            PasskeyCeremony ceremony = await PasskeyCeremonies.BeginAddingAsync(_host.MemberClient);
            (await PasskeyCeremonies.TryAddAsync(_host.MemberClient, ceremony, _authenticator)).EnsureSuccessStatusCode();

            using SoftwareAuthenticator another = new SoftwareAuthenticator();
            HttpResponseMessage again = await PasskeyCeremonies.TryAddAsync(_host.MemberClient, ceremony, another);

            Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        }

        [Fact]
        public async Task SignIn_Succeeding_SetsTheFileCookieAndClearsTheFailedSignIns()
        {
            List<bool> succeeded = new List<bool>();
            await using ServerTestHost host = await ServerTestHost.StartWithPasskeysAsync(services =>
                services.AddSingleton<IStartupFilter>(new SignInOutcomeRecorder(succeeded)));
            await PasskeyCeremonies.AddAsync(host.MemberClient, _authenticator);

            HttpResponseMessage response = await PasskeyCeremonies.TrySignInAsync(host.AnonymousClient, _authenticator);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains(response.Headers.GetValues("Set-Cookie"), cookie => cookie.Contains("access_token", StringComparison.Ordinal));
            Assert.Equal(new[] { true }, succeeded);
        }

        [Fact]
        public async Task SignIn_CredentialThatIsNoAssertion_Returns400()
        {
            PasskeyCeremony ceremony = await PasskeyCeremonies.BeginSignInAsync(_host.AnonymousClient);

            HttpResponseMessage response = await _host.AnonymousClient.PostAsJsonAsync("/api/auth/passkey", new { ceremonyId = ceremony.CeremonyId, credential = new { id = "x" } });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task BeginAdding_WrongPassword_Returns401WithCode()
        {
            HttpResponseMessage response = await PasskeyCeremonies.TryBeginAddingAsync(_host.MemberClient, "not-the-password");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal(ErrorCodes.InvalidCredentials, (await response.Content.ReadFromJsonAsync<ErrorResponse>())?.Code);
        }

        [Fact]
        public async Task BeginAdding_ExcludesThePasskeysTheUserHas()
        {
            await PasskeyCeremonies.AddAsync(_host.MemberClient, _authenticator);

            PasskeyCeremony ceremony = await PasskeyCeremonies.BeginAddingAsync(_host.MemberClient);

            string excluded = ceremony.Options.GetProperty("excludeCredentials").EnumerateArray().Single().GetProperty("id").GetString()!;
            Assert.Equal(Base64Url.EncodeToString(_authenticator.CredentialId), excluded);
        }

        [Fact]
        public async Task Add_PasskeyAlreadyAdded_Returns409()
        {
            await PasskeyCeremonies.AddAsync(_host.MemberClient, _authenticator);

            HttpResponseMessage again = await PasskeyCeremonies.TryAddAsync(
                _host.MemberClient, await PasskeyCeremonies.BeginAddingAsync(_host.MemberClient), _authenticator);

            Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
            Assert.Single((await ListAsync(_host.MemberClient)).Passkeys);
        }

        [Theory]
        [InlineData("   ")]
        [InlineData(TooLongName)]
        public async Task Add_NameBreakingTheRule_Returns400(string name)
        {
            HttpResponseMessage response = await PasskeyCeremonies.TryAddAsync(
                _host.MemberClient, await PasskeyCeremonies.BeginAddingAsync(_host.MemberClient), _authenticator, name);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Empty((await ListAsync(_host.MemberClient)).Passkeys);
        }

        [Theory]
        [InlineData("")]
        [InlineData(TooLongName)]
        public async Task Rename_NameBreakingTheRule_Returns400(string name)
        {
            PasskeyInfo added = await PasskeyCeremonies.AddAsync(_host.MemberClient, _authenticator);

            HttpResponseMessage response = await _host.MemberClient.PutAsJsonAsync($"/api/users/me/passkeys/{added.Id}/name", new { name });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(added.Name, (await ListAsync(_host.MemberClient)).Passkeys.Single().Name);
        }

        [Fact]
        public async Task List_ShowsTheUsersOwnPasskeysWithTheirLastUse()
        {
            using SoftwareAuthenticator adminAuthenticator = new SoftwareAuthenticator();
            await PasskeyCeremonies.AddAsync(_host.AdminClient, adminAuthenticator, "Admin's phone");
            PasskeyInfo added = await PasskeyCeremonies.AddAsync(_host.MemberClient, _authenticator, "  Work laptop ");
            Assert.Null((await ListAsync(_host.MemberClient)).Passkeys.Single().LastUsedAt);

            await PasskeyCeremonies.SignInAsync(_host.AnonymousClient, _authenticator);

            PasskeyList list = await ListAsync(_host.MemberClient);
            Assert.True(list.Available && list.CanAdd);
            PasskeyInfo listed = Assert.Single(list.Passkeys);
            Assert.Equal((added.Id, "Work laptop"), (listed.Id, listed.Name));
            Assert.NotNull(listed.LastUsedAt);
        }

        [Fact]
        public async Task Rename_OwnPasskey_RenamesIt()
        {
            PasskeyInfo added = await PasskeyCeremonies.AddAsync(_host.MemberClient, _authenticator);

            HttpResponseMessage response = await _host.MemberClient.PutAsJsonAsync($"/api/users/me/passkeys/{added.Id}/name", new { name = "Old phone" });

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Equal("Old phone", (await ListAsync(_host.MemberClient)).Passkeys.Single().Name);
        }

        [Fact]
        public async Task RenameAndRemove_AnotherUsersPasskey_Return404AndLeaveIt()
        {
            PasskeyInfo added = await PasskeyCeremonies.AddAsync(_host.MemberClient, _authenticator);

            HttpResponseMessage renamed = await _host.AdminClient.PutAsJsonAsync($"/api/users/me/passkeys/{added.Id}/name", new { name = "Mine now" });
            HttpResponseMessage removed = await _host.AdminClient.DeleteAsync($"/api/users/me/passkeys/{added.Id}");

            Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (renamed.StatusCode, removed.StatusCode));
            Assert.Equal(added.Name, (await ListAsync(_host.MemberClient)).Passkeys.Single().Name);
        }

        [Fact]
        public async Task Remove_EndsTheSessionsSignedInWithIt_AtOnce()
        {
            PasskeyInfo added = await PasskeyCeremonies.AddAsync(_host.MemberClient, _authenticator);
            SessionTokens tokens = await PasskeyCeremonies.SignInAsync(_host.AnonymousClient, _authenticator);

            Assert.Equal(HttpStatusCode.NoContent, (await _host.MemberClient.DeleteAsync($"/api/users/me/passkeys/{added.Id}")).StatusCode);

            using HttpClient session = _host.CreateClient(tokens.Token);
            Assert.Equal(HttpStatusCode.Unauthorized, (await session.GetAsync("/api/users/me")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(tokens.RefreshToken)).StatusCode);

            // The password session it was removed from carries on.
            Assert.Equal(HttpStatusCode.OK, (await _host.MemberClient.GetAsync("/api/users/me")).StatusCode);
        }

        [Fact]
        public async Task SignIn_RemovedPasskey_Returns401()
        {
            PasskeyInfo added = await PasskeyCeremonies.AddAsync(_host.MemberClient, _authenticator);
            await _host.MemberClient.DeleteAsync($"/api/users/me/passkeys/{added.Id}");

            HttpResponseMessage response = await PasskeyCeremonies.TrySignInAsync(_host.AnonymousClient, _authenticator);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Remove_ARenewedSessionSignedInWithIt_EndsIt()
        {
            PasskeyInfo added = await PasskeyCeremonies.AddAsync(_host.MemberClient, _authenticator);
            SessionTokens signedIn = await PasskeyCeremonies.SignInAsync(_host.AnonymousClient, _authenticator);
            SessionTokens renewed = (await (await RefreshAsync(signedIn.RefreshToken)).Content.ReadFromJsonAsync<SessionTokens>())!;

            await _host.MemberClient.DeleteAsync($"/api/users/me/passkeys/{added.Id}");

            using HttpClient session = _host.CreateClient(renewed.Token);
            Assert.Equal(HttpStatusCode.Unauthorized, (await session.GetAsync("/api/users/me")).StatusCode);
        }

        [Fact]
        public async Task PasswordChangedFromAPasswordSession_EndsPasskeySessions()
        {
            await PasskeyCeremonies.AddAsync(_host.MemberClient, _authenticator);
            SessionTokens tokens = await PasskeyCeremonies.SignInAsync(_host.AnonymousClient, _authenticator);

            (await _host.MemberClient.PutAsJsonAsync("/api/users/me/password", new { currentPassword = ServerTestHost.Password, newPassword = "a-new-password" }))
                .EnsureSuccessStatusCode();

            Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(tokens.RefreshToken)).StatusCode);
        }

        [Fact]
        public async Task PasswordChangedFromAPasskeySession_KeepsItTiedToThePasskey()
        {
            PasskeyInfo added = await PasskeyCeremonies.AddAsync(_host.MemberClient, _authenticator);
            SessionTokens tokens = await PasskeyCeremonies.SignInAsync(_host.AnonymousClient, _authenticator);
            using HttpClient session = _host.CreateClient(tokens.Token);
            (await session.PutAsJsonAsync("/api/users/me/password", new { currentPassword = ServerTestHost.Password, newPassword = "a-new-password" }))
                .EnsureSuccessStatusCode();
            SessionTokens renewed = (await (await RefreshAsync(tokens.RefreshToken)).Content.ReadFromJsonAsync<SessionTokens>())!;

            await _host.MemberClient.DeleteAsync($"/api/users/me/passkeys/{added.Id}");

            Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(renewed.RefreshToken)).StatusCode);
        }

        [Fact]
        public async Task PasswordReset_EndsPasskeySessionsAndKeepsThePasskeys()
        {
            await PasskeyCeremonies.AddAsync(_host.MemberClient, _authenticator);
            SessionTokens tokens = await PasskeyCeremonies.SignInAsync(_host.AnonymousClient, _authenticator);

            using (IServiceScope scope = _host.Services.CreateScope())
            {
                Assert.True(await scope.ServiceProvider.GetRequiredService<IPasswordResetService>().SetPasswordAsync(_host.MemberId, "a-new-password"));
            }

            using HttpClient session = _host.CreateClient(tokens.Token);
            Assert.Equal(HttpStatusCode.Unauthorized, (await session.GetAsync("/api/users/me")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(tokens.RefreshToken)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await PasskeyCeremonies.TrySignInAsync(_host.AnonymousClient, _authenticator)).StatusCode);
        }

        [Fact]
        public async Task DeletingAUser_RemovesTheirPasskeys()
        {
            PasskeyInfo added = await PasskeyCeremonies.AddAsync(_host.MemberClient, _authenticator);

            (await _host.AdminClient.DeleteAsync($"/api/users/{_host.MemberId}")).EnsureSuccessStatusCode();

            ChangeLogEntry removal = (await PasskeyEntriesAsync()).Last();
            Assert.Equal((added.Id, ChangeAction.Purged), (removal.EntityId, removal.Action));
        }

        [Fact]
        public async Task ChangeLog_HoldsAddingRenamingAndRemovingButNotSigningIn()
        {
            PasskeyInfo added = await PasskeyCeremonies.AddAsync(_host.MemberClient, _authenticator, "Laptop");
            await _host.MemberClient.PutAsJsonAsync($"/api/users/me/passkeys/{added.Id}/name", new { name = "Old laptop" });
            _authenticator.SignCount = 7;
            await PasskeyCeremonies.SignInAsync(_host.AnonymousClient, _authenticator);
            await _host.MemberClient.DeleteAsync($"/api/users/me/passkeys/{added.Id}");

            List<ChangeLogEntry> entries = await PasskeyEntriesAsync();

            Assert.Equal(new[] { ChangeAction.Added, ChangeAction.Updated, ChangeAction.Purged }, entries.Select(entry => entry.Action));
            Assert.All(entries, entry => Assert.Equal(added.Id, entry.EntityId));
            Passkey renamed = ChangePayload.Deserialize<Passkey>(entries[1].Payload!);
            Assert.Equal((_host.MemberId, "Old laptop"), (renamed.UserId, renamed.Name));
            Assert.Equal(SoftwareAuthenticator.Transports, renamed.Transports);
            Assert.DoesNotContain("signCount", entries[1].Payload!, StringComparison.OrdinalIgnoreCase);
            Assert.Null(entries[2].Payload);
        }

        [Fact]
        public async Task PasskeysOff_WithoutAPublicAddress_NoCeremonyBegins()
        {
            await using ServerTestHost withoutAddress = await ServerTestHost.StartAsync();

            Assert.Equal(HttpStatusCode.NotFound, (await withoutAddress.AnonymousClient.PostAsync("/api/auth/passkey/ceremony", content: null)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await PasskeyCeremonies.TryBeginAddingAsync(withoutAddress.MemberClient)).StatusCode);
            PasskeyList list = await ListAsync(withoutAddress.MemberClient);
            Assert.False(list.Available || list.CanAdd);
        }

        /// <summary>One character past the most a passkey's name can have.</summary>
        private const string TooLongName = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

        private async Task<List<ChangeLogEntry>> PasskeyEntriesAsync()
        {
            using IServiceScope scope = _host.Services.CreateScope();
            return (await scope.ServiceProvider.GetRequiredService<IChangeLogRepository>().GetAfterAsync(0, 1000))
                .Where(entry => entry.EntityType == ChangeEntityType.Passkey)
                .ToList();
        }

        private static async Task<PasskeyList> ListAsync(HttpClient client)
        {
            return (await client.GetFromJsonAsync<PasskeyList>("/api/users/me/passkeys"))!;
        }

        private Task<HttpResponseMessage> RefreshAsync(string refreshToken)
        {
            return _host.AnonymousClient.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest { RefreshToken = refreshToken });
        }

        /// <summary>Records, for each passkey sign-in, whether it marked the request as authenticated, which clears the address's failed sign-ins.</summary>
        private sealed class SignInOutcomeRecorder(List<bool> succeeded) : IStartupFilter
        {
            public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
            {
                return app =>
                {
                    app.Use(async (context, nextMiddleware) =>
                    {
                        await nextMiddleware(context);
                        if (context.Request.Path == "/api/auth/passkey")
                        {
                            succeeded.Add(context.HasAuthenticationSucceeded());
                        }
                    });
                    next(app);
                };
            }
        }
    }
}
