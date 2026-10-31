using ByteShelf.Server.Access.Models;
using ByteShelf.Server.Access.Repositories;
using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Repositories;
using ByteShelf.Server.Users.Models;
using ByteShelf.Server.Users.Repositories;
using ByteShelfCommon;
using EasyReasy.Database;

namespace ByteShelf.Server.Access.Services
{
    /// <inheritdoc />
    public class ShelfGrantService : IShelfGrantService
    {
        private readonly IDbSessionFactory _sessionFactory;
        private readonly IShelfGrantRepository _grantRepository;
        private readonly IShelfRepository _shelfRepository;
        private readonly IUserRepository _userRepository;
        private readonly TimeProvider _timeProvider;

        public ShelfGrantService(
            IDbSessionFactory sessionFactory,
            IShelfGrantRepository grantRepository,
            IShelfRepository shelfRepository,
            IUserRepository userRepository,
            TimeProvider timeProvider)
        {
            _sessionFactory = sessionFactory;
            _grantRepository = grantRepository;
            _shelfRepository = shelfRepository;
            _userRepository = userRepository;
            _timeProvider = timeProvider;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<ShelfAccessGrant>> GetForShelfAsync(Shelf shelf)
        {
            IReadOnlyList<HeldShelfGrant> grants = await _grantRepository.GetForShelfAsync(shelf.Id);
            return grants.Select(held => AccessContracts.ToShelfAccessGrant(held.Grant, held.Email, held.DisplayName)).ToList();
        }

        /// <inheritdoc />
        public async Task<ShelfAccessGrant> SetAsync(Shelf shelf, Guid userId, ShelfAccessPermission permission)
        {
            if (!Enum.IsDefined(permission))
            {
                throw new ArgumentException($"'{permission}' is not a shelf access permission.");
            }

            await using (IDbTransactionSession session = await _sessionFactory.CreateSessionWithTransactionAsync())
            {
                if (await _shelfRepository.GetByIdAsync(shelf.Id, session) == null)
                {
                    throw new NotFoundException($"There is no shelf '{shelf.Name}'.");
                }

                User user = await _userRepository.GetByIdAsync(userId, session)
                    ?? throw new NotFoundException($"There is no user {userId}.");

                // Two calls for the same user cannot both insert: the transaction holds SQLite's one write
                // lock from its first statement, so the second waits and then finds the first one's grant.
                DateTimeOffset now = _timeProvider.GetUtcNow();
                ShelfGrant? existing = await _grantRepository.GetAsync(shelf.Id, userId, session);
                ShelfGrant grant = new ShelfGrant
                {
                    Id = existing?.Id ?? Guid.NewGuid(),
                    ShelfId = shelf.Id,
                    UserId = userId,
                    Permission = permission,
                    CreatedAt = existing?.CreatedAt ?? now,
                    UpdatedAt = now,
                };

                if (existing == null)
                {
                    await _grantRepository.InsertAsync(grant, session);
                }
                else if (existing.Permission != permission)
                {
                    await _grantRepository.UpdateAsync(grant, session);
                }
                else
                {
                    grant = existing;
                }

                await session.CommitAsync();
                return AccessContracts.ToShelfAccessGrant(grant, user.Email, user.DisplayName);
            }
        }

        /// <inheritdoc />
        public async Task RemoveAsync(Shelf shelf, Guid userId)
        {
            await using (IDbTransactionSession session = await _sessionFactory.CreateSessionWithTransactionAsync())
            {
                if (!await _grantRepository.DeleteAsync(shelf.Id, userId, _timeProvider.GetUtcNow(), session))
                {
                    throw new NotFoundException($"User {userId} has no access to the shelf '{shelf.Name}'.");
                }

                await session.CommitAsync();
            }
        }
    }
}
