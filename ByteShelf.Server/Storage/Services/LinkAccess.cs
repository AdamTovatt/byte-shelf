using ByteShelf.Server.Access.Models;
using ByteShelf.Server.Access.Services;
using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Storage.Models;

namespace ByteShelf.Server.Storage.Services
{
    /// <summary>
    /// The access a link works with: its maker's, as it is now, checked as for any request each time the
    /// link is used. A link stops working while its maker may no longer do what it does, and works again
    /// if that access comes back before it runs out.
    /// </summary>
    public sealed class LinkAccess
    {
        private readonly ShelfCallers _callers;
        private readonly IShelfAccessResolver _accessResolver;
        private readonly IShelfService _shelfService;
        private readonly ShelfEncryption _encryption;

        public LinkAccess(ShelfCallers callers, IShelfAccessResolver accessResolver, IShelfService shelfService, ShelfEncryption encryption)
        {
            _callers = callers;
            _accessResolver = accessResolver;
            _shelfService = shelfService;
            _encryption = encryption;
        }

        /// <summary>
        /// The maker's access to the link's shelf, once it is known the maker may still do what the link
        /// does: read, for a download link; for an upload link, store as it does, at a place that takes a
        /// plain file. Whether a download link's file may be served is the caller's to check, on the file.
        /// </summary>
        /// <exception cref="NotFoundException">The maker no longer has access to the shelf; <paramref name="notFoundMessage"/>, which tells the holder no more than that.</exception>
        /// <exception cref="PermissionDeniedException">The maker may no longer store there, or overwrite when the link does.</exception>
        /// <exception cref="ConflictException">An upload link's place is under an encrypted directory or holds an encrypted file.</exception>
        public async Task<ShelfAccess> ResolveAsync(LinkRecord link, string notFoundMessage)
        {
            // The row goes with its maker or shelf, so either is only missing when it went since the row was read.
            ShelfCaller caller = await _callers.FindAsync(link.CreatedBy) ?? throw new NotFoundException(notFoundMessage);
            ShelfAccess access;
            try
            {
                access = await _accessResolver.ResolveAsync(caller, await _shelfService.GetByIdAsync(link.ShelfId));
            }
            catch (NotFoundException)
            {
                // Not the resolver's message, which names the shelf to someone who may never have known it.
                throw new NotFoundException(notFoundMessage);
            }

            switch (link.Target)
            {
                case DownloadLinkTarget:
                    access.Require(ShelfOperation.Read);
                    break;
                case UploadLinkTarget upload:
                    access.RequireStore(upload.Overwrite);

                    // Checked again: the place may have been encrypted since the link was made.
                    await _encryption.RequireLinkTargetAsync(access.Shelf.Id, upload.Path);
                    break;
            }

            return access;
        }
    }
}
