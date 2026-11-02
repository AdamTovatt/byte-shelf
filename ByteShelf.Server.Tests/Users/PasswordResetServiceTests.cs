using ByteShelf.Server.ChangeLog.Models;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelf.Server.Users.Models;
using ByteShelf.Server.Users.Repositories;
using ByteShelf.Server.Users.Services;
using EasyReasy.Auth;
using EasyReasy.Database;
using Microsoft.Extensions.DependencyInjection;

namespace ByteShelf.Server.Tests.Users
{
    public class PasswordResetServiceTests : ServiceTestBase
    {
        private const string Email = "known@example.com";

        private IPasswordResetService ResetService => Services.GetRequiredService<IPasswordResetService>();
        private IUserService UserService => Services.GetRequiredService<IUserService>();

        [Fact]
        public async Task RequestResetAsync_UnknownEmail_QueuesNothing()
        {
            await ResetService.RequestResetAsync("unknown@example.com");

            Assert.Null(await TryTakeLinkAsync());
        }

        [Fact]
        public async Task RequestResetAsync_KnownEmail_QueuesALinkWhoseTokenSetsThePassword()
        {
            await UserService.CreateUserAsync(Email, "Known", "old-password");

            await ResetService.RequestResetAsync(Email);
            ResetLink link = (await TryTakeLinkAsync())!;
            bool reset = await ResetService.CompleteResetAsync(link.Token, "new-password");

            Assert.Equal(Email, link.Email);
            Assert.True(reset);
            Assert.NotNull(await UserService.ValidateCredentialsAsync(Email, "new-password"));
        }

        [Fact]
        public async Task RequestResetAsync_Twice_LeavesTheFirstLinkWorking()
        {
            await UserService.CreateUserAsync(Email, "Known", "old-password");
            await ResetService.RequestResetAsync(Email);
            ResetLink first = (await TryTakeLinkAsync())!;

            await ResetService.RequestResetAsync(Email);

            Assert.True(await ResetService.CompleteResetAsync(first.Token, "new-password"));
        }

        [Fact]
        public async Task CompleteResetAsync_OneLinkUsed_InvalidatesTheOthers()
        {
            await UserService.CreateUserAsync(Email, "Known", "old-password");
            await ResetService.RequestResetAsync(Email);
            ResetLink first = (await TryTakeLinkAsync())!;
            await ResetService.RequestResetAsync(Email);
            ResetLink second = (await TryTakeLinkAsync())!;

            await ResetService.CompleteResetAsync(second.Token, "new-password");

            Assert.False(await ResetService.CompleteResetAsync(first.Token, "other-password"));
        }

        [Fact]
        public async Task CompleteResetAsync_UnknownToken_ReturnsFalse()
        {
            Assert.False(await ResetService.CompleteResetAsync("not-a-real-token", "new-password"));
        }

        [Fact]
        public async Task CompleteResetAsync_TooShortPassword_Throws()
        {
            await Assert.ThrowsAsync<ArgumentException>(() => ResetService.CompleteResetAsync("any-token", "short"));
        }

        [Fact]
        public async Task CompleteResetAsync_ExpiredToken_ReturnsFalseAndKeepsThePassword()
        {
            await UserService.CreateUserAsync(Email, "Known", "old-password");
            await ResetService.RequestResetAsync(Email);
            ResetLink link = (await TryTakeLinkAsync())!;
            Clock.Advance(PasswordResetService.TokenLifetime + TimeSpan.FromSeconds(1));

            bool reset = await ResetService.CompleteResetAsync(link.Token, "new-password");

            Assert.False(reset);
            Assert.NotNull(await UserService.ValidateCredentialsAsync(Email, "old-password"));
        }

        [Fact]
        public async Task CompleteResetAsync_TokenUsedBefore_ReturnsFalseAndKeepsTheFirstNewPassword()
        {
            await UserService.CreateUserAsync(Email, "Known", "old-password");
            await ResetService.RequestResetAsync(Email);
            ResetLink link = (await TryTakeLinkAsync())!;
            await ResetService.CompleteResetAsync(link.Token, "first-new-password");

            bool reset = await ResetService.CompleteResetAsync(link.Token, "second-new-password");

            Assert.False(reset);
            Assert.NotNull(await UserService.ValidateCredentialsAsync(Email, "first-new-password"));
        }

        [Fact]
        public async Task CompleteResetAsync_ValidToken_EndsEverySession()
        {
            User user = await UserService.CreateUserAsync(Email, "Known", "old-password");
            IRefreshTokenService refreshTokens = Services.GetRequiredService<IRefreshTokenService>();
            RefreshTokenCreationResult session = await refreshTokens.CreateRefreshTokenWithFamilyAsync(
                user.Id.ToString("D"), AuthRequestValidationService.AuthType, null, RefreshTokenClaims.SerializeRoles(Array.Empty<string>()));
            await ResetService.RequestResetAsync(Email);
            ResetLink link = (await TryTakeLinkAsync())!;

            await ResetService.CompleteResetAsync(link.Token, "new-password");

            StoredRefreshToken? stored = await Services.GetRequiredService<IRefreshTokenStore>().GetByTokenHashAsync(HashToken(session.RawToken));
            Assert.True(stored?.IsInvalidated);
        }

        [Fact]
        public async Task SetPasswordAsync_SetsItAndEndsEverySessionAndResetLink()
        {
            User user = await UserService.CreateUserAsync(Email, "Known", "old-password");
            RefreshTokenCreationResult session = await Services.GetRequiredService<IRefreshTokenService>().CreateRefreshTokenWithFamilyAsync(
                user.Id.ToString("D"), AuthRequestValidationService.AuthType, null, RefreshTokenClaims.SerializeRoles(Array.Empty<string>()));
            await ResetService.RequestResetAsync(Email);
            ResetLink link = (await TryTakeLinkAsync())!;

            Assert.True(await ResetService.SetPasswordAsync(user.Id, "new-password"));

            Assert.Equal(user.Id, (await UserService.ValidateCredentialsAsync(Email, "new-password"))?.User.Id);
            StoredRefreshToken? stored = await Services.GetRequiredService<IRefreshTokenStore>().GetByTokenHashAsync(HashToken(session.RawToken));
            Assert.True(stored?.IsInvalidated);
            Assert.False(await ResetService.CompleteResetAsync(link.Token, "other-password"));
        }

        [Fact]
        public async Task SetPasswordAsync_UnknownUser_ReturnsFalse()
        {
            Assert.False(await ResetService.SetPasswordAsync(Guid.NewGuid(), "new-password"));
        }

        [Fact]
        public async Task SetPasswordAsync_TooShortPassword_ThrowsAndKeepsTheOldOne()
        {
            User user = await UserService.CreateUserAsync(Email, "Known", "old-password");

            await Assert.ThrowsAsync<ArgumentException>(() => ResetService.SetPasswordAsync(user.Id, "short"));

            Assert.NotNull(await UserService.ValidateCredentialsAsync(Email, "old-password"));
        }

        [Fact]
        public async Task SetPasswordAsync_DroppingTheSessionsFails_StillReportsTheNewPassword()
        {
            User user = await UserService.CreateUserAsync(Email, "Known", "old-password");
            IPasswordResetService resetService = ActivatorUtilities.CreateInstance<PasswordResetService>(Services, new EndingSessionsFails(UserService));

            Assert.True(await resetService.SetPasswordAsync(user.Id, "new-password"));

            Assert.NotNull(await UserService.ValidateCredentialsAsync(Email, "new-password"));
        }

        [Fact]
        public async Task SetPasswordAsync_LogsTheLoginWithTheNewHash()
        {
            User user = await UserService.CreateUserAsync(Email, "Known", "old-password");
            long before = await ChangeLog.GetLatestSequenceAsync();

            await ResetService.SetPasswordAsync(user.Id, "new-password");

            ChangeLogEntry entry = Assert.Single(await ChangeLog.GetAfterAsync(before, 100));
            Assert.Equal((ChangeEntityType.UserLogin, ChangeAction.Updated), (entry.EntityType, entry.Action));
            UserCredentials stored = (await Services.GetRequiredService<IUserLoginRepository>().GetCredentialsByEmailAsync(Email))!;
            Assert.Equal(stored.PasswordHash, ChangePayload.Deserialize<UserLogin>(entry.Payload!).Credential);
        }

        [Fact]
        public async Task CompleteResetAsync_ValidToken_LogsTheLoginWithTheNewHash()
        {
            await UserService.CreateUserAsync(Email, "Known", "old-password");
            await ResetService.RequestResetAsync(Email);
            ResetLink link = (await TryTakeLinkAsync())!;
            long before = await ChangeLog.GetLatestSequenceAsync();

            await ResetService.CompleteResetAsync(link.Token, "new-password");

            ChangeLogEntry entry = Assert.Single(await ChangeLog.GetAfterAsync(before, 100));
            Assert.Equal((ChangeEntityType.UserLogin, ChangeAction.Updated), (entry.EntityType, entry.Action));
            UserCredentials stored = (await Services.GetRequiredService<IUserLoginRepository>().GetCredentialsByEmailAsync(Email))!;
            Assert.Equal(stored.PasswordHash, ChangePayload.Deserialize<UserLogin>(entry.Payload!).Credential);
        }

        /// <summary>The next queued link, or null when none arrives promptly.</summary>
        private async Task<ResetLink?> TryTakeLinkAsync()
        {
            using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            try
            {
                await foreach (ResetLink link in Services.GetRequiredService<ResetLinkOutbox>().ReadAllAsync(timeout.Token))
                {
                    return link;
                }
            }
            catch (OperationCanceledException)
            {
            }

            return null;
        }

        private static string HashToken(string rawToken)
        {
            return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawToken)));
        }

        /// <summary>The real user service, except that ending a user's sessions fails.</summary>
        private sealed class EndingSessionsFails(IUserService inner) : IUserService
        {
            public Task<int> EndSessionsAsync(Guid userId)
            {
                throw new InvalidOperationException("The refresh token store is unreachable.");
            }

            public Task<UserCredentials?> ValidateCredentialsAsync(string email, string password)
            {
                return inner.ValidateCredentialsAsync(email, password);
            }

            public Task<User?> GetUserByIdAsync(Guid userId)
            {
                return inner.GetUserByIdAsync(userId);
            }

            public Task<User?> GetUserByEmailAsync(string email)
            {
                return inner.GetUserByEmailAsync(email);
            }

            public Task<User> CreateUserAsync(string email, string displayName, string password, IEnumerable<string>? roles = null)
            {
                return inner.CreateUserAsync(email, displayName, password, roles);
            }

            public Task<bool> UpdatePasswordAsync(Guid userId, string currentPassword, string newPassword, string? keepSessionFamilyId = null)
            {
                return inner.UpdatePasswordAsync(userId, currentPassword, newPassword, keepSessionFamilyId);
            }

            public Task<IReadOnlyList<User>> GetAllUsersAsync()
            {
                return inner.GetAllUsersAsync();
            }

            public Task UpdateRolesAsync(Guid userId, IEnumerable<string> roles)
            {
                return inner.UpdateRolesAsync(userId, roles);
            }

            public Task DeleteUserAsync(Guid userId)
            {
                return inner.DeleteUserAsync(userId);
            }

            public Task<bool> RemoveUserAsync(Guid userId, DateTimeOffset at, IDbSession session)
            {
                return inner.RemoveUserAsync(userId, at, session);
            }

            public Task<int> GetUserCountAsync()
            {
                return inner.GetUserCountAsync();
            }
        }
    }
}
