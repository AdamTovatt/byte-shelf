using ByteShelf.Server.Access.Models;
using ByteShelf.Server.Access.Repositories;
using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Shared.Validation;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Repositories;
using ByteShelfCommon;
using EasyReasy.Database;

namespace ByteShelf.Server.Access.Services
{
    /// <inheritdoc />
    public class ShelfKeyService : IShelfKeyService
    {
        private readonly IDbSessionFactory _sessionFactory;
        private readonly IShelfKeyRepository _keyRepository;
        private readonly IShelfRepository _shelfRepository;
        private readonly TimeProvider _timeProvider;

        public ShelfKeyService(
            IDbSessionFactory sessionFactory,
            IShelfKeyRepository keyRepository,
            IShelfRepository shelfRepository,
            TimeProvider timeProvider)
        {
            _sessionFactory = sessionFactory;
            _keyRepository = keyRepository;
            _shelfRepository = shelfRepository;
            _timeProvider = timeProvider;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<ShelfKeyInfo>> GetForShelfAsync(Shelf shelf)
        {
            IReadOnlyList<ShelfKey> keys = await _keyRepository.GetForShelfAsync(shelf.Id);
            return keys.Select(AccessContracts.ToShelfKeyInfo).ToList();
        }

        /// <inheritdoc />
        public async Task<CreatedShelfKey> CreateAsync(Shelf shelf, string name, ShelfKeyPermission permission)
        {
            string trimmedName = DisplayText.Validate(name, ShelfKeys.MaxNameLength, "A key's name");
            if (!Enum.IsDefined(permission))
            {
                throw new ArgumentException($"'{permission}' is not a shelf key permission.");
            }

            string secret = ShelfKeySecrets.Generate();
            ShelfKey key = new ShelfKey
            {
                Id = Guid.NewGuid(),
                ShelfId = shelf.Id,
                Name = trimmedName,
                Permission = permission,
                SecretSha256 = ShelfKeySecrets.Hash(secret),
                CreatedAt = _timeProvider.GetUtcNow(),
            };

            await using (IDbTransactionSession session = await _sessionFactory.CreateSessionWithTransactionAsync())
            {
                if (await _shelfRepository.GetByIdAsync(shelf.Id, session) == null)
                {
                    throw new NotFoundException($"There is no shelf '{shelf.Name}'.");
                }

                await _keyRepository.InsertAsync(key, session);
                await session.CommitAsync();
            }

            return new CreatedShelfKey { Key = AccessContracts.ToShelfKeyInfo(key), Secret = secret };
        }

        /// <inheritdoc />
        public async Task DeleteAsync(Shelf shelf, Guid keyId)
        {
            await using (IDbTransactionSession session = await _sessionFactory.CreateSessionWithTransactionAsync())
            {
                if (!await _keyRepository.DeleteAsync(shelf.Id, keyId, _timeProvider.GetUtcNow(), session))
                {
                    throw new NotFoundException($"The shelf '{shelf.Name}' has no key {keyId}.");
                }

                await session.CommitAsync();
            }
        }

        /// <inheritdoc />
        public async Task<ShelfKey?> FindBySecretAsync(string secret)
        {
            return await _keyRepository.GetBySecretSha256Async(ShelfKeySecrets.Hash(secret));
        }
    }
}
