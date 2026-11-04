using System.Globalization;
using System.Security.Claims;
using ByteShelf.Server.Users.Models;
using ByteShelf.Server.Users.Repositories;
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

        /// <summary>The claim EasyReasy.Auth puts a session's family on refreshed tokens under, and <c>GetRefreshFamilyId</c> reads.</summary>
        private const string FamilyIdClaimType = "family_id";

        private readonly IUserService _userService;
        private readonly IUserLoginRepository _userLoginRepository;
        private readonly IRefreshTokenService _refreshTokenService;
        private readonly TimeProvider _timeProvider;

        public AuthRequestValidationService(
            IUserService userService,
            IUserLoginRepository userLoginRepository,
            IRefreshTokenService refreshTokenService,
            TimeProvider timeProvider)
        {
            _userService = userService;
            _userLoginRepository = userLoginRepository;
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
            UserCredentials? credentials = await _userService.ValidateCredentialsAsync(request.Username, request.Password);
            if (credentials == null)
            {
                return LoginResult.Failed(LoginFailureReason.InvalidCredentials, attemptedSubject: request.Username);
            }

            User user = credentials.User;
            string subject = user.Id.ToString("D");
            string[] roles = user.Roles.ToArray();

            RefreshTokenCreationResult refreshTokenResult = await _refreshTokenService.CreateRefreshTokenWithFamilyAsync(
                subject: subject,
                authType: AuthType,
                serializedClaims: CredentialStamp.SerializeClaimsFor(credentials.PasswordHash),
                serializedRoles: RefreshTokenClaims.SerializeRoles(roles));

            // A reset that committed after the password was checked ended the sessions there were, which
            // did not yet include this one: the sign-in is refused rather than handed out.
            UserCredentials? current = await _userLoginRepository.GetCredentialsByUserIdAsync(user.Id);
            if (current == null || current.PasswordHash != credentials.PasswordHash)
            {
                return LoginResult.Failed(LoginFailureReason.InvalidCredentials, attemptedSubject: request.Username);
            }

            // The session's family rides on its access token from the start, as it does on refreshed
            // ones, so a password change made with this token can keep this session.
            DateTime expiresAt = _timeProvider.GetUtcNow().UtcDateTime.Add(AccessTokenLifetime);
            string accessToken = jwtTokenService.CreateToken(
                subject: subject,
                authType: AuthType,
                additionalClaims: new[] { new Claim(FamilyIdClaimType, refreshTokenResult.FamilyId) },
                roles: roles,
                expiresAt: expiresAt);

            AuthResponse authResponse = new AuthResponse(accessToken, expiresAt.ToString("o", CultureInfo.InvariantCulture), refreshTokenResult.RawToken);
            return LoginResult.Succeeded(authResponse, subject: subject);
        }
    }
}
