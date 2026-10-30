using System.Globalization;
using System.Security.Claims;
using ByteShelf.Server.Users.Models;
using EasyReasy.Auth;

namespace ByteShelf.Server.Users.Services
{
    /// <summary>
    /// Validates sign-in requests and issues a JWT + refresh token pair. The credential carried by the
    /// login request is the <b>email</b> (EasyReasy.Auth's request field is named <c>Username</c>).
    /// Expiry comes from the injected <see cref="TimeProvider"/>.
    /// </summary>
    public class AuthRequestValidationService : IAuthRequestValidationService
    {
        public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromHours(1);

        public const string AuthType = "user";

        private readonly IUserService _userService;
        private readonly IRefreshTokenService _refreshTokenService;
        private readonly TimeProvider _timeProvider;

        public AuthRequestValidationService(
            IUserService userService,
            IRefreshTokenService refreshTokenService,
            TimeProvider timeProvider)
        {
            _userService = userService;
            _refreshTokenService = refreshTokenService;
            _timeProvider = timeProvider;
        }

        /// <summary>
        /// Programs authenticate with shelf keys sent on each request, not through this endpoint, which
        /// is not mapped; answers a failure if it is ever called.
        /// </summary>
        public Task<ApiKeyAuthResult> ValidateApiKeyRequestAsync(
            ApiKeyAuthRequest request,
            IJwtTokenService jwtTokenService,
            HttpContext? httpContext = null)
        {
            return Task.FromResult(ApiKeyAuthResult.Failed(ApiKeyAuthFailureReason.UnknownKey, attemptedClientId: null));
        }

        /// <inheritdoc />
        public async Task<LoginResult> ValidateLoginRequestAsync(
            LoginAuthRequest request,
            IJwtTokenService jwtTokenService,
            HttpContext? httpContext = null)
        {
            User? user = await _userService.ValidateCredentialsAsync(request.Username, request.Password);
            if (user == null)
            {
                return LoginResult.Failed(LoginFailureReason.InvalidCredentials, attemptedSubject: request.Username);
            }

            string subject = user.Id.ToString("D");
            string[] roles = user.Roles.ToArray();

            RefreshTokenCreationResult refreshTokenResult = await _refreshTokenService.CreateRefreshTokenWithFamilyAsync(
                subject: subject,
                authType: AuthType,
                serializedClaims: null,
                serializedRoles: RefreshTokenClaims.SerializeRoles(roles));

            DateTime expiresAt = _timeProvider.GetUtcNow().UtcDateTime.Add(AccessTokenLifetime);
            string accessToken = jwtTokenService.CreateToken(
                subject: subject,
                authType: AuthType,
                additionalClaims: Array.Empty<Claim>(),
                roles: roles,
                expiresAt: expiresAt);

            AuthResponse authResponse = new AuthResponse(accessToken, expiresAt.ToString("o", CultureInfo.InvariantCulture), refreshTokenResult.RawToken);
            return LoginResult.Succeeded(authResponse, subject: subject);
        }
    }
}
