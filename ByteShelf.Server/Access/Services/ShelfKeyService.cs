using ByteShelf.Server.Access.Models;
using ByteShelf.Server.Access.Repositories;
using ByteShelf.Server.Shared.Auth;
using ByteShelf.Server.Shared.Errors;
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
        private readonly IShelfKeyGrantRepository _grantRepository;
        private readonly ShelfKeyRemoval _keyRemoval;
        private readonly IShelfRepository _shelfRepository;
        private readonly TimeProvider _timeProvider;

        public ShelfKeyService(
            IDbSessionFactory sessionFactory,
            IShelfKeyRepository keyRepository,
            IShelfKeyGrantRepository grantRepository,
            ShelfKeyRemoval keyRemoval,
            IShelfRepository shelfRepository,
            TimeProvider timeProvider)
        {
            _sessionFactory = sessionFactory;
            _keyRepository = keyRepository;
            _grantRepository = grantRepository;
            _keyRemoval = keyRemoval;
            _shelfRepository = shelfRepository;
            _timeProvider = timeProvider;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<ShelfKeyInfo>> GetForShelfAsync(Shelf shelf)
        {
            // Two reads: a key deleted between them is listed without shelves, as it was just before.
            IReadOnlyList<ShelfKey> keys = await _keyRepository.GetForShelfAsync(shelf.Id);
            ILookup<Guid, HeldShelfKeyGrant> grants = (await _grantRepository.GetForKeysOnShelfAsync(shelf.Id)).ToLookup(held => held.Grant.KeyId);

            return keys.Select(key => AccessContracts.ToShelfKeyInfo(key, grants[key.Id])).ToList();
        }

        /// <inheritdoc />
        public async Task<CreatedShelfKey> CreateAsync(Shelf shelf, string name, ShelfPermission permission)
        {
            string trimmedName = DisplayText.Validate(name, ShelfKeys.MaxNameLength, "A key's name");
            ShelfKeys.ValidatePermission(permission);

            string secret = RandomSecret.Generate(ShelfKeys.SecretPrefix);
            DateTimeOffset now = _timeProvider.GetUtcNow();
            ShelfKey key = new ShelfKey
            {
                Id = Guid.NewGuid(),
                Name = trimmedName,
                SecretSha256 = SecretHash.Sha256Hex(secret),
                CreatedAt = now,
            };

            ShelfKeyGrant grant = new ShelfKeyGrant
            {
                Id = Guid.NewGuid(),
                KeyId = key.Id,
                ShelfId = shelf.Id,
                Permission = permission,
                CreatedAt = now,
                UpdatedAt = now,
            };

            await using (IDbTransactionSession session = await _sessionFactory.CreateSessionWithTransactionAsync())
            {
                Shelf current = await GetForWriteAsync(shelf, session);

                await _keyRepository.InsertAsync(key, session);
                await _grantRepository.InsertAsync(grant, session);
                await session.CommitAsync();

                HeldShelfKeyGrant held = new HeldShelfKeyGrant { Grant = grant, ShelfName = current.Name };
                return new CreatedShelfKey { Key = AccessContracts.ToShelfKeyInfo(key, new[] { held }), Secret = secret };
            }
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<ShelfKeyGrantInfo>> GetGrantsAsync(Guid keyId)
        {
            await using (IDbSession session = await _sessionFactory.CreateSessionAsync())
            {
                await GetKeyAsync(keyId, session);
                IReadOnlyList<HeldShelfKeyGrant> grants = await _grantRepository.GetForKeyAsync(keyId, session);
                return grants.Select(AccessContracts.ToShelfKeyGrantInfo).ToList();
            }
        }

        /// <inheritdoc />
        public async Task<ShelfKeyGrantInfo> SetGrantAsync(Shelf shelf, Guid keyId, ShelfPermission permission)
        {
            ShelfKeys.ValidatePermission(permission);

            await using (IDbTransactionSession session = await _sessionFactory.CreateSessionWithTransactionAsync())
            {
                Shelf current = await GetForWriteAsync(shelf, session);
                await GetKeyAsync(keyId, session);

                // Two calls for the same key and shelf cannot both insert: the transaction holds SQLite's one
                // write lock from its first statement, so the second waits and then finds the first one's grant.
                DateTimeOffset now = _timeProvider.GetUtcNow();
                ShelfKeyGrant? existing = await _grantRepository.GetAsync(shelf.Id, keyId, session);
                ShelfKeyGrant grant = new ShelfKeyGrant
                {
                    Id = existing?.Id ?? Guid.NewGuid(),
                    KeyId = keyId,
                    ShelfId = shelf.Id,
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
                return AccessContracts.ToShelfKeyGrantInfo(new HeldShelfKeyGrant { Grant = grant, ShelfName = current.Name });
            }
        }

        /// <inheritdoc />
        public async Task RemoveGrantAsync(Shelf shelf, Guid keyId)
        {
            await using (IDbTransactionSession session = await _sessionFactory.CreateSessionWithTransactionAsync())
            {
                if (!await _keyRemoval.RemoveGrantAsync(shelf.Id, keyId, _timeProvider.GetUtcNow(), session))
                {
                    throw new NotFoundException($"The shelf '{shelf.Name}' has no key {keyId}.");
                }

                await session.CommitAsync();
            }
        }

        /// <inheritdoc />
        public async Task DeleteAsync(Guid keyId)
        {
            await using (IDbTransactionSession session = await _sessionFactory.CreateSessionWithTransactionAsync())
            {
                if (!await _keyRemoval.DeleteKeyAsync(keyId, _timeProvider.GetUtcNow(), session))
                {
                    throw new NotFoundException($"There is no key {keyId}.");
                }

                await session.CommitAsync();
            }
        }

        /// <inheritdoc />
        public async Task<ShelfKey?> FindBySecretAsync(string secret)
        {
            return await _keyRepository.GetBySecretSha256Async(SecretHash.Sha256Hex(secret));
        }

        private async Task<ShelfKey> GetKeyAsync(Guid keyId, IDbSession session)
        {
            return await _keyRepository.GetByIdAsync(keyId, session)
                ?? throw new NotFoundException($"There is no key {keyId}.");
        }

        /// <summary>The shelf as it is inside the write transaction; it may have been deleted since it was looked up.</summary>
        private async Task<Shelf> GetForWriteAsync(Shelf shelf, IDbSession session)
        {
            return await _shelfRepository.GetByIdAsync(shelf.Id, session)
                ?? throw new NotFoundException($"There is no shelf '{shelf.Name}'.");
        }
    }
}
