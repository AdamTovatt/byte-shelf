using ByteShelf.Server.Access.Models;
using ByteShelf.Server.Access.Repositories;
using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Repositories;
using ByteShelfCommon;

namespace ByteShelf.Server.Access.Services
{
    /// <inheritdoc />
    public class ShelfAccessResolver : IShelfAccessResolver
    {
        private static readonly IReadOnlySet<ShelfOperation> Nothing = new HashSet<ShelfOperation>();
        private static readonly IReadOnlySet<ShelfOperation> ReadOnly = new HashSet<ShelfOperation> { ShelfOperation.Read };
        private static readonly IReadOnlySet<ShelfOperation> AddOnly = new HashSet<ShelfOperation> { ShelfOperation.Read, ShelfOperation.Add };
        private static readonly IReadOnlySet<ShelfOperation> ReadWrite = new HashSet<ShelfOperation> { ShelfOperation.Read, ShelfOperation.Add, ShelfOperation.Write };
        private static readonly IReadOnlySet<ShelfOperation> Everything = new HashSet<ShelfOperation>(Enum.GetValues<ShelfOperation>());

        private readonly IShelfGrantRepository _grantRepository;
        private readonly IShelfKeyGrantRepository _keyGrantRepository;
        private readonly IShelfRepository _shelfRepository;

        public ShelfAccessResolver(IShelfGrantRepository grantRepository, IShelfKeyGrantRepository keyGrantRepository, IShelfRepository shelfRepository)
        {
            _grantRepository = grantRepository;
            _keyGrantRepository = keyGrantRepository;
            _shelfRepository = shelfRepository;
        }

        /// <inheritdoc />
        public async Task<ShelfAccess> ResolveAsync(ShelfCaller caller, Shelf shelf)
        {
            ShelfAccess? access = await GetAccessOnOneAsync(caller, shelf);
            return access ?? throw new NotFoundException($"There is no shelf '{shelf.Name}'.");
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<ShelfAccess>> GetAccessesAsync(ShelfCaller caller)
        {
            IReadOnlyList<Shelf> shelves = await _shelfRepository.GetAllAsync();
            Func<Shelf, ShelfAccess?> accessOn = await GetAccessOnEveryAsync(caller);
            return shelves.Select(accessOn).OfType<ShelfAccess>().ToList();
        }

        /// <summary>The caller's access to one shelf, with the one lookup its grant there needs; null for none.</summary>
        private async Task<ShelfAccess?> GetAccessOnOneAsync(ShelfCaller caller, Shelf shelf)
        {
            if (caller.Key != null)
            {
                return ForKey(caller, shelf, await _keyGrantRepository.GetAsync(shelf.Id, caller.Key.Id));
            }

            if (caller.IsAdmin)
            {
                return new ShelfAccess(shelf, caller, Everything);
            }

            return ForUser(caller, shelf, await _grantRepository.GetAsync(shelf.Id, caller.UserId!.Value));
        }

        /// <summary>The caller's access to each shelf, with every grant it holds read at once.</summary>
        private async Task<Func<Shelf, ShelfAccess?>> GetAccessOnEveryAsync(ShelfCaller caller)
        {
            if (caller.Key != null)
            {
                Dictionary<Guid, ShelfKeyGrant> keyGrants = (await _keyGrantRepository.GetForKeyAsync(caller.Key.Id))
                    .ToDictionary(held => held.Grant.ShelfId, held => held.Grant);
                return shelf => ForKey(caller, shelf, keyGrants.GetValueOrDefault(shelf.Id));
            }

            if (caller.IsAdmin)
            {
                return shelf => new ShelfAccess(shelf, caller, Everything);
            }

            Dictionary<Guid, ShelfGrant> grants = (await _grantRepository.GetForUserAsync(caller.UserId!.Value)).ToDictionary(grant => grant.ShelfId);
            return shelf => ForUser(caller, shelf, grants.GetValueOrDefault(shelf.Id));
        }

        /// <summary>A key's access to a shelf given its grant there; none without one.</summary>
        private static ShelfAccess? ForKey(ShelfCaller caller, Shelf shelf, ShelfKeyGrant? grant)
        {
            IReadOnlySet<ShelfOperation> allowed = grant?.Permission switch
            {
                null => Nothing,
                ShelfKeyPermission.Read => ReadOnly,
                ShelfKeyPermission.AddOnly => AddOnly,
                ShelfKeyPermission.ReadWrite => ReadWrite,
                _ => throw new InvalidOperationException($"No access is defined for a {grant.Permission} key."),
            };

            return allowed.Count == 0 ? null : new ShelfAccess(shelf, caller, allowed, grant!.Permission);
        }

        /// <summary>A user's (not an admin's) access to a shelf given their grant there; none without one.</summary>
        private static ShelfAccess? ForUser(ShelfCaller caller, Shelf shelf, ShelfGrant? grant)
        {
            IReadOnlySet<ShelfOperation> allowed = grant?.Permission switch
            {
                null => Nothing,
                ShelfAccessPermission.Read => ReadOnly,
                ShelfAccessPermission.ReadWrite => ReadWrite,
                _ => throw new InvalidOperationException($"No access is defined for {grant.Permission} access."),
            };

            return allowed.Count == 0 ? null : new ShelfAccess(shelf, caller, allowed);
        }
    }
}
