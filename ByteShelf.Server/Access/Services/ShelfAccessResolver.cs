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
        private readonly IShelfRepository _shelfRepository;

        public ShelfAccessResolver(IShelfGrantRepository grantRepository, IShelfRepository shelfRepository)
        {
            _grantRepository = grantRepository;
            _shelfRepository = shelfRepository;
        }

        /// <inheritdoc />
        public async Task<ShelfAccess> ResolveAsync(ShelfCaller caller, Shelf shelf)
        {
            ShelfGrant? grant = NeedsGrant(caller) ? await _grantRepository.GetAsync(shelf.Id, caller.UserId!.Value) : null;

            IReadOnlySet<ShelfOperation> allowed = GetAllowed(caller, shelf, grant);
            if (allowed.Count == 0)
            {
                throw new NotFoundException($"There is no shelf '{shelf.Name}'.");
            }

            return new ShelfAccess(shelf, caller, allowed);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<Shelf>> GetVisibleShelvesAsync(ShelfCaller caller)
        {
            IReadOnlyList<Shelf> shelves = await _shelfRepository.GetAllAsync();

            Dictionary<Guid, ShelfGrant> grants = NeedsGrant(caller)
                ? (await _grantRepository.GetForUserAsync(caller.UserId!.Value)).ToDictionary(grant => grant.ShelfId)
                : new Dictionary<Guid, ShelfGrant>();

            return shelves.Where(shelf => GetAllowed(caller, shelf, grants.GetValueOrDefault(shelf.Id)).Count > 0).ToList();
        }

        /// <summary>Whether what the caller may do on a shelf depends on a grant: only a user who is not an admin.</summary>
        private static bool NeedsGrant(ShelfCaller caller)
        {
            return caller.Key == null && !caller.IsAdmin;
        }

        /// <param name="grant">The caller's grant on the shelf, looked up when <see cref="NeedsGrant"/>.</param>
        private static IReadOnlySet<ShelfOperation> GetAllowed(ShelfCaller caller, Shelf shelf, ShelfGrant? grant)
        {
            if (caller.Key != null)
            {
                if (caller.Key.ShelfId != shelf.Id)
                {
                    return Nothing;
                }

                return caller.Key.Permission switch
                {
                    ShelfKeyPermission.Read => ReadOnly,
                    ShelfKeyPermission.AddOnly => AddOnly,
                    ShelfKeyPermission.ReadWrite => ReadWrite,
                    _ => throw new InvalidOperationException($"No access is defined for a {caller.Key.Permission} key."),
                };
            }

            if (caller.IsAdmin)
            {
                return Everything;
            }

            return grant?.Permission switch
            {
                null => Nothing,
                ShelfAccessPermission.Read => ReadOnly,
                ShelfAccessPermission.ReadWrite => ReadWrite,
                _ => throw new InvalidOperationException($"No access is defined for {grant.Permission} access."),
            };
        }
    }
}
