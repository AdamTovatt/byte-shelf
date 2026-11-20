using ByteShelf.Server.Access.Models;
using ByteShelf.Server.Access.Repositories;
using EasyReasy.Database;

namespace ByteShelf.Server.Access.Services
{
    /// <summary>
    /// How keys and their grants go, in the caller's transaction: the one home for the rules that a key's
    /// grants go before the key (they refer to it) and that a key goes with its last grant (a key always
    /// works somewhere). Each removal is logged by the repositories.
    /// </summary>
    public sealed class ShelfKeyRemoval
    {
        private readonly IShelfKeyRepository _keyRepository;
        private readonly IShelfKeyGrantRepository _grantRepository;

        public ShelfKeyRemoval(IShelfKeyRepository keyRepository, IShelfKeyGrantRepository grantRepository)
        {
            _keyRepository = keyRepository;
            _grantRepository = grantRepository;
        }

        /// <summary>Takes the shelf from the key, deleting the key when that was its last grant. False when it had no grant there.</summary>
        public async Task<bool> RemoveGrantAsync(Guid shelfId, Guid keyId, DateTimeOffset at, IDbSession session)
        {
            if (!await _grantRepository.DeleteAsync(shelfId, keyId, at, session))
            {
                return false;
            }

            await _keyRepository.DeleteIfUngrantedAsync(keyId, at, session);
            return true;
        }

        /// <summary>Takes the shelf from every key, deleting each key it was the last shelf of: for deleting the shelf.</summary>
        public async Task RemoveGrantsOnShelfAsync(Guid shelfId, DateTimeOffset at, IDbSession session)
        {
            foreach (Guid keyId in await _grantRepository.DeleteForShelfAsync(shelfId, at, session))
            {
                await _keyRepository.DeleteIfUngrantedAsync(keyId, at, session);
            }
        }

        /// <summary>Deletes the key and its grants. False when there is no such key.</summary>
        public async Task<bool> DeleteKeyAsync(Guid keyId, DateTimeOffset at, IDbSession session)
        {
            await _grantRepository.DeleteForKeyAsync(keyId, at, session);
            return await _keyRepository.DeleteByIdAsync(keyId, at, session);
        }

        /// <summary>
        /// Writes a key as a primary's change log holds it, logged as <paramref name="action"/>. A different
        /// key with the same secret is deleted first, with its grants: the log brings them back if they
        /// still exist.
        /// </summary>
        public async Task UpsertReplicatedKeyAsync(ShelfKey key, string action, DateTimeOffset at, IDbSession session)
        {
            if (await _keyRepository.GetBySecretSha256Async(key.SecretSha256, session) is ShelfKey other && other.Id != key.Id)
            {
                await DeleteKeyAsync(other.Id, at, session);
            }

            await _keyRepository.UpsertReplicatedAsync(key, action, at, session);
        }
    }
}
