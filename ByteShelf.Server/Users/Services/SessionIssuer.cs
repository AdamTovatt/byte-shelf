using System.Globalization;
using System.Security.Claims;
using ByteShelf.Server.Users.Models;
using ByteShelf.Server.Users.Repositories;
using EasyReasy.Auth;

namespace ByteShelf.Server.Users.Services
{
    /// <summary>
    /// Starts a session for a user whose sign-in has been checked, by password or by passkey: a refresh
    /// token family stamped with what they signed in with (<see cref="CredentialStamp"/>) and a 1-hour JWT,
    /// expiring by the injected <see cref="TimeProvider"/>.
    /// </summary>
    public class SessionIssuer
    {
        public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromHours(1);

        public const string AuthType = "user";

        /// <summary>
        /// The claim EasyReasy.Auth puts a session's family on refreshed tokens under, and <c>GetRefreshFamilyId</c>
        /// reads. Spelled out here because the library has no public name for it: a public constant, or a
        /// library call that starts a session (refresh family, stored claims and first access token) for
        /// password and passkey sign-in alike, would replace it.
        /// </summary>
        private const string FamilyIdClaimType = "family_id";

        private readonly IUserLoginRepository _userLoginRepository;
        private readonly IRefreshTokenService _refreshTokenService;
        private readonly TimeProvider _timeProvider;

        public SessionIssuer(IUserLoginRepository userLoginRepository, IRefreshTokenService refreshTokenService, TimeProvider timeProvider)
        {
            _userLoginRepository = userLoginRepository;
            _refreshTokenService = refreshTokenService;
            _timeProvider = timeProvider;
        }

        /// <summary>
        /// The tokens of a new session for <paramref name="credentials"/>' user, as checked against the
        /// password hash they hold, and signed in with <paramref name="passkeyId"/> when it was a passkey.
        /// Null when the password has changed since that was read: a reset that committed meanwhile ended
        /// the sessions there were, which did not yet include this one, so it is refused rather than handed out.
        /// </summary>
        public async Task<AuthResponse?> IssueAsync(UserCredentials credentials, Guid? passkeyId, IJwtTokenService jwtTokenService)
        {
            User user = credentials.User;
            string subject = user.Id.ToString("D");
            string[] roles = user.Roles.ToArray();

            RefreshTokenCreationResult refreshTokenResult = await _refreshTokenService.CreateRefreshTokenWithFamilyAsync(
                subject: subject,
                authType: AuthType,
                serializedClaims: CredentialStamp.SerializeClaimsFor(credentials.PasswordHash, passkeyId),
                serializedRoles: RefreshTokenClaims.SerializeRoles(roles));

            UserCredentials? current = await _userLoginRepository.GetCredentialsByUserIdAsync(user.Id);
            if (current == null || current.PasswordHash != credentials.PasswordHash)
            {
                return null;
            }

            // The first access token carries what refreshed ones do, the stored claims and the session's
            // family: a password change made with it can keep this session, and it ends with its passkey.
            List<Claim> claims = new List<Claim>(CredentialStamp.ClaimsFor(credentials.PasswordHash, passkeyId))
            {
                new Claim(FamilyIdClaimType, refreshTokenResult.FamilyId),
            };

            DateTime expiresAt = _timeProvider.GetUtcNow().UtcDateTime.Add(AccessTokenLifetime);
            string accessToken = jwtTokenService.CreateToken(
                subject: subject,
                authType: AuthType,
                additionalClaims: claims,
                roles: roles,
                expiresAt: expiresAt);

            return new AuthResponse(accessToken, expiresAt.ToString("o", CultureInfo.InvariantCulture), refreshTokenResult.RawToken);
        }
    }
}
