using ByteShelf.Server.Users.Models;
using ByteShelf.Server.Users.Repositories;
using EasyReasy.Auth;

namespace ByteShelf.Server.Users.Services
{
    /// <summary>
    /// Puts the user's roles as they are now on every refreshed token, rather than the ones stored when
    /// they signed in, and refuses the refresh once the user no longer exists or their password is not
    /// the one the session was signed in with (<see cref="CredentialStamp"/>).
    /// </summary>
    public class CurrentRolesRefreshResolver : IRefreshClaimsResolver
    {
        private readonly IUserLoginRepository _loginRepository;

        public CurrentRolesRefreshResolver(IUserLoginRepository loginRepository)
        {
            _loginRepository = loginRepository;
        }

        public async Task<RefreshClaimsDecision> ResolveAsync(RefreshClaimsContext context, CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(context.Subject, out Guid userId))
            {
                return RefreshClaimsDecision.Deny();
            }

            UserCredentials? credentials = await _loginRepository.GetCredentialsByUserIdAsync(userId);
            if (credentials == null || !CredentialStamp.Matches(context.StoredClaims, credentials.PasswordHash))
            {
                return RefreshClaimsDecision.Deny();
            }

            return RefreshClaimsDecision.Allow(context.StoredClaims, credentials.User.Roles);
        }
    }
}
