using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using ByteShelf.Server.Shared.Auth;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelf.Server.Users.Models;
using ByteShelf.Server.Users.Repositories;
using ByteShelf.Server.Users.Services;
using ByteShelfCommon;
using EasyReasy.Auth;
using EasyReasy.Database;
using Microsoft.Extensions.DependencyInjection;

namespace ByteShelf.Server.Tests.Users
{
    public class AuthRequestValidationServiceTests : ServiceTestBase
    {
        private IAuthRequestValidationService Validation => Services.GetRequiredService<IAuthRequestValidationService>();
        private IJwtTokenService Jwt => Services.GetRequiredService<IJwtTokenService>();
        private IUserService UserService => Services.GetRequiredService<IUserService>();

        [Fact]
        public async Task ValidateLoginRequestAsync_InvalidCredentials_ReturnsFailedResult()
        {
            await UserService.CreateUserAsync("user@example.com", "User", "correct-password");

            LoginResult result = await Validation.ValidateLoginRequestAsync(new LoginAuthRequest("user@example.com", "wrong-password"), Jwt);

            Assert.False(result.Success);
        }

        [Fact]
        public async Task ValidateLoginRequestAsync_ValidCredentials_IssuesTokensExpiringOneHourFromNow()
        {
            User user = await UserService.CreateUserAsync("user@example.com", "User", "correct-password");

            LoginResult result = await Validation.ValidateLoginRequestAsync(new LoginAuthRequest("user@example.com", "correct-password"), Jwt);

            Assert.True(result.Success);
            Assert.Equal(user.Id.ToString("D"), result.AttemptedSubject);
            Assert.False(string.IsNullOrEmpty(result.AuthResponse!.RefreshToken));
            DateTimeOffset expiresAt = DateTimeOffset.Parse(result.AuthResponse.ExpiresAt, CultureInfo.InvariantCulture);
            Assert.Equal(TestInstants.Now + AuthRequestValidationService.AccessTokenLifetime, expiresAt);
        }

        [Fact]
        public async Task ValidateApiKeyRequestAsync_Always_ReturnsFailedResult()
        {
            ApiKeyAuthResult result = await Validation.ValidateApiKeyRequestAsync(new ApiKeyAuthRequest("any-key"), Jwt);

            Assert.False(result.Success);
        }

        [Fact]
        public async Task CurrentRolesRefreshResolver_RolesChangedSinceSignIn_GivesTheCurrentRoles()
        {
            await UserService.CreateUserAsync("first@example.com", "First", "password123", new[] { UserRoles.Admin });
            User user = await UserService.CreateUserAsync("user@example.com", "User", "password123", new[] { UserRoles.Admin });
            IReadOnlyList<Claim> stamp = await CurrentStampAsync(user.Id);
            await UserService.UpdateRolesAsync(user.Id, Array.Empty<string>());

            RefreshClaimsDecision decision = await ResolveRefreshAsync(user.Id, stamp);

            Assert.False(decision.IsDenied);
            Assert.Empty(decision.Roles);
        }

        [Fact]
        public async Task CurrentRolesRefreshResolver_PasswordChangedSinceSignIn_DeniesTheRefresh()
        {
            User user = await UserService.CreateUserAsync("user@example.com", "User", "old-password");
            IReadOnlyList<Claim> stamp = await CurrentStampAsync(user.Id);

            // What a reset committing between a sign-in's password check and its token being stored
            // leaves behind: a session stamped with the credential the reset replaced.
            await UserService.UpdatePasswordAsync(user.Id, "old-password", "new-password");

            Assert.True((await ResolveRefreshAsync(user.Id, stamp)).IsDenied);
        }

        [Fact]
        public async Task CurrentRolesRefreshResolver_SessionWithoutAStamp_DeniesTheRefresh()
        {
            User user = await UserService.CreateUserAsync("user@example.com", "User", "password123");

            Assert.True((await ResolveRefreshAsync(user.Id, Array.Empty<Claim>())).IsDenied);
        }

        [Fact]
        public async Task CurrentRolesRefreshResolver_SamePasswordSetAgain_StillDeniesTheRefresh()
        {
            User user = await UserService.CreateUserAsync("user@example.com", "User", "password123");
            IReadOnlyList<Claim> stamp = await CurrentStampAsync(user.Id);

            await UserService.UpdatePasswordAsync(user.Id, "password123", "password123");

            Assert.True((await ResolveRefreshAsync(user.Id, stamp)).IsDenied);
        }

        [Fact]
        public async Task ValidateLoginRequestAsync_SessionStoredAfterAResetCommitted_IsRefused()
        {
            await UserService.CreateUserAsync("user@example.com", "User", "old-password");
            Services.GetRequiredService<ResettingTokenStore>().ResetOnNextStore = "new-password";

            LoginResult result = await Validation.ValidateLoginRequestAsync(new LoginAuthRequest("user@example.com", "old-password"), Jwt);

            Assert.False(result.Success);
        }

        [Fact]
        public async Task ValidateLoginRequestAsync_ValidCredentials_StampsTheSessionWithTheCredential()
        {
            User user = await UserService.CreateUserAsync("user@example.com", "User", "correct-password");

            LoginResult result = await Validation.ValidateLoginRequestAsync(new LoginAuthRequest("user@example.com", "correct-password"), Jwt);

            StoredRefreshToken stored = (await Services.GetRequiredService<IRefreshTokenStore>().GetByTokenHashAsync(SecretHash.Sha256Hex(result.AuthResponse!.RefreshToken!)))!;
            Assert.Equal(await CurrentStampAsync(user.Id), RefreshTokenClaims.DeserializeClaims(stored.SerializedClaims), ClaimComparer);
        }

        protected override void ConfigureServices(IServiceCollection services)
        {
            // Stands in the store's place, so a test can change the password between a sign-in's check
            // and its token being stored, as a reset committing at that moment would.
            services.AddScoped<ResettingTokenStore>();
            services.AddScoped<IRefreshTokenStore>(provider => provider.GetRequiredService<ResettingTokenStore>());
        }

        private static IEqualityComparer<Claim> ClaimComparer { get; } = EqualityComparer<Claim>.Create(
            (left, right) => left?.Type == right?.Type && left?.Value == right?.Value,
            claim => HashCode.Combine(claim.Type, claim.Value));

        private async Task<IReadOnlyList<Claim>> CurrentStampAsync(Guid userId)
        {
            UserCredentials credentials = (await Services.GetRequiredService<IUserLoginRepository>().GetCredentialsByUserIdAsync(userId))!;
            return CredentialStamp.ClaimsFor(credentials.PasswordHash);
        }

        private async Task<RefreshClaimsDecision> ResolveRefreshAsync(Guid userId, IReadOnlyList<Claim> storedClaims)
        {
            return await Services.GetRequiredService<IRefreshClaimsResolver>().ResolveAsync(
                new RefreshClaimsContext(userId.ToString("D"), AuthRequestValidationService.AuthType, storedClaims, new[] { UserRoles.Admin }, null),
                CancellationToken.None);
        }

        /// <summary>The real store, which sets a new password for the token's user first when asked to.</summary>
        private sealed class ResettingTokenStore : RefreshTokenStore, IRefreshTokenStore
        {
            private readonly IUserLoginRepository _logins;
            private readonly IPasswordHasher _hasher;
            private readonly IDbSessionFactory _sessionFactory;

            public ResettingTokenStore(DbDataSource dataSource, IDbSessionFactory sessionFactory, IUserLoginRepository logins, IPasswordHasher hasher)
                : base(dataSource, sessionFactory)
            {
                _logins = logins;
                _hasher = hasher;
                _sessionFactory = sessionFactory;
            }

            /// <summary>The password the next stored token's user gets just before it is stored.</summary>
            public string? ResetOnNextStore { get; set; }

            public new async Task StoreAsync(StoredRefreshToken refreshToken, CancellationToken cancellationToken = default)
            {
                if (ResetOnNextStore != null)
                {
                    await using IDbTransactionSession session = await _sessionFactory.CreateSessionWithTransactionAsync();
                    await _logins.UpdateCredentialAsync(Guid.Parse(refreshToken.Subject), UserLoginRepository.LocalProvider, _hasher.HashPassword(ResetOnNextStore), TestInstants.Now, session);
                    await session.CommitAsync();
                    ResetOnNextStore = null;
                }

                await base.StoreAsync(refreshToken, cancellationToken);
            }
        }

        [Fact]
        public async Task CurrentRolesRefreshResolver_UserGone_DeniesTheRefresh()
        {
            RefreshClaimsDecision decision = await Services.GetRequiredService<IRefreshClaimsResolver>().ResolveAsync(
                new RefreshClaimsContext(Guid.NewGuid().ToString("D"), AuthRequestValidationService.AuthType, Array.Empty<Claim>(), new[] { UserRoles.Admin }, null),
                CancellationToken.None);

            Assert.True(decision.IsDenied);
        }
    }
}
