using ByteShelf.Server.Access.Models;
using ByteShelf.Server.Access.Services;
using ByteShelf.Server.Invalidation.Models;
using ByteShelf.Server.Users.Models;
using ByteShelf.Server.Users.Repositories;
using ByteShelfCommon;

namespace ByteShelf.Server.Invalidation.Services
{
    /// <summary>
    /// Which events a signed-in user's stream carries. An event naming a shelf goes only to a user who
    /// can see that shelf when it is sent, by the rule <see cref="IShelfAccessResolver"/> holds, so a
    /// stream tells nobody the ids of shelves they cannot see, or when those change. The other events
    /// name nothing and go to everyone.
    /// </summary>
    public sealed class InvalidationAudience
    {
        private readonly IUserRepository _userRepository;
        private readonly IShelfAccessResolver _accessResolver;

        public InvalidationAudience(IUserRepository userRepository, IShelfAccessResolver accessResolver)
        {
            _userRepository = userRepository;
            _accessResolver = accessResolver;
        }

        /// <summary>
        /// The events in <paramref name="events"/> the user may hear of, in order; null once the user no
        /// longer exists. The user's roles and grants are read as they are now, so a stream opened before
        /// a change follows it.
        /// </summary>
        public async Task<IReadOnlyList<InvalidationEvent>?> FilterAsync(Guid userId, IReadOnlyList<InvalidationEvent> events)
        {
            if (!events.Any(NamesAShelf))
            {
                return events;
            }

            User? user = await _userRepository.GetByIdAsync(userId);
            if (user == null)
            {
                return null;
            }

            ShelfCaller caller = ShelfCaller.ForUser(userId, user.Roles.Contains(UserRoles.Admin));
            HashSet<string> visible = (await _accessResolver.GetVisibleShelvesAsync(caller))
                .Select(shelf => shelf.Id.ToString("D"))
                .ToHashSet(StringComparer.Ordinal);

            return events.Where(invalidation => !NamesAShelf(invalidation) || visible.Contains(invalidation.Metadata!)).ToList();
        }

        private static bool NamesAShelf(InvalidationEvent invalidation)
        {
            return invalidation.Metadata != null
                && invalidation.Resource is InvalidationResources.ShelfContents or InvalidationResources.ShelfAccess;
        }
    }
}
