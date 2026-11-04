using ByteShelf.Server.Access.Models;
using ByteShelf.Server.Access.Services;
using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Storage.Models;

namespace ByteShelf.Server.Storage.Services
{
    /// <summary>The access an upload link stores with: its maker's, as it is now, checked as for any request.</summary>
    public sealed class UploadLinkAccess
    {
        private readonly ShelfCallers _callers;
        private readonly IShelfAccessResolver _accessResolver;
        private readonly IShelfService _shelfService;
        private readonly ShelfEncryption _encryption;

        public UploadLinkAccess(ShelfCallers callers, IShelfAccessResolver accessResolver, IShelfService shelfService, ShelfEncryption encryption)
        {
            _callers = callers;
            _accessResolver = accessResolver;
            _shelfService = shelfService;
            _encryption = encryption;
        }

        /// <summary>
        /// The maker's access to the link's shelf, once it is known the maker may still store as the link
        /// does, and the place takes a plain file.
        /// </summary>
        /// <exception cref="NotFoundException">The maker or the shelf is gone, or the maker no longer has access to it.</exception>
        /// <exception cref="PermissionDeniedException">The maker may no longer store there, or overwrite when the link does.</exception>
        /// <exception cref="ConflictException">The place is under an encrypted directory or holds an encrypted file.</exception>
        public async Task<ShelfAccess> ResolveAsync(UploadLinkGrant grant, string notFoundMessage)
        {
            ShelfCaller caller = await _callers.FindAsync(grant.Caller) ?? throw new NotFoundException(notFoundMessage);
            Shelf shelf = await _shelfService.GetByIdAsync(grant.ShelfId);
            ShelfAccess access = await _accessResolver.ResolveAsync(caller, shelf);
            access.RequireStore(grant.Overwrite);

            // Checked again: the place may have been encrypted since the link was made.
            await _encryption.RequireLinkTargetAsync(shelf.Id, grant.Path);
            return access;
        }
    }
}
