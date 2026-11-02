using ByteShelf.Server.Access.Models;
using ByteShelf.Server.Access.Repositories;
using ByteShelf.Server.Users.Models;
using ByteShelf.Server.Users.Repositories;

namespace ByteShelf.Server.Access.Services
{
    /// <summary>Finds a caller again by its <see cref="ShelfCallerReference"/>, for what acts later on its behalf.</summary>
    public sealed class ShelfCallers
    {
        private readonly IShelfKeyRepository _keyRepository;
        private readonly IUserRepository _userRepository;

        public ShelfCallers(IShelfKeyRepository keyRepository, IUserRepository userRepository)
        {
            _keyRepository = keyRepository;
            _userRepository = userRepository;
        }

        /// <summary>The caller as it is now, with a user's current roles; null once the key or user is deleted.</summary>
        public async Task<ShelfCaller?> FindAsync(ShelfCallerReference reference)
        {
            if (reference.Kind == ShelfCallerKind.Key)
            {
                ShelfKey? key = await _keyRepository.GetByIdAsync(reference.Id);
                return key == null ? null : ShelfCaller.ForKey(key);
            }

            User? user = await _userRepository.GetByIdAsync(reference.Id);
            return user == null ? null : ShelfCaller.ForUser(user);
        }
    }
}
