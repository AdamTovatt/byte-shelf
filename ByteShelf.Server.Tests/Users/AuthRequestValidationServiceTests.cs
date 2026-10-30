using System.Globalization;
using System.Security.Claims;
using ByteShelf.Server.Shared.Auth;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelf.Server.Users.Models;
using ByteShelf.Server.Users.Services;
using EasyReasy.Auth;
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
            await UserService.CreateUserAsync("first@example.com", "First", "password123", new[] { UserRole.Admin });
            User user = await UserService.CreateUserAsync("user@example.com", "User", "password123", new[] { UserRole.Admin });
            await UserService.UpdateRolesAsync(user.Id, Array.Empty<string>());

            RefreshClaimsDecision decision = await Services.GetRequiredService<IRefreshClaimsResolver>().ResolveAsync(
                new RefreshClaimsContext(user.Id.ToString("D"), AuthRequestValidationService.AuthType, Array.Empty<Claim>(), new[] { UserRole.Admin }, null),
                CancellationToken.None);

            Assert.False(decision.IsDenied);
            Assert.Empty(decision.Roles);
        }

        [Fact]
        public async Task CurrentRolesRefreshResolver_UserGone_DeniesTheRefresh()
        {
            RefreshClaimsDecision decision = await Services.GetRequiredService<IRefreshClaimsResolver>().ResolveAsync(
                new RefreshClaimsContext(Guid.NewGuid().ToString("D"), AuthRequestValidationService.AuthType, Array.Empty<Claim>(), new[] { UserRole.Admin }, null),
                CancellationToken.None);

            Assert.True(decision.IsDenied);
        }
    }
}
