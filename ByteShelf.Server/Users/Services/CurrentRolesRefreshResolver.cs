using System.Security.Claims;
using ByteShelf.Server.Users.Models;
using ByteShelf.Server.Users.Repositories;
using EasyReasy.Auth;

namespace ByteShelf.Server.Users.Services
{
    /// <summary>
    /// Puts the user's roles as they are now on every refreshed token, rather than the ones stored when
    /// they signed in, and refuses the refresh once the user no longer exists. A role taken away
    /// therefore reaches a signed-in user within one access-token lifetime.
    /// </summary>
    public class CurrentRolesRefreshResolver : IRefreshClaimsResolver
    {
        private readonly IUserRepository _userRepository;

        public CurrentRolesRefreshResolver(IUserRepository userRepository)
        {
            _userRepository = userRepository;
        }

        public async Task<RefreshClaimsDecision> ResolveAsync(RefreshClaimsContext context, CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(context.Subject, out Guid userId))
            {
                return RefreshClaimsDecision.Deny();
            }

            User? user = await _userRepository.GetByIdAsync(userId);
            if (user == null)
            {
                return RefreshClaimsDecision.Deny();
            }

            return RefreshClaimsDecision.Allow(Array.Empty<Claim>(), user.Roles);
        }
    }
}
