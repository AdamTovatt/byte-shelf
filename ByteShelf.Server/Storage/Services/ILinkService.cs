using ByteShelf.Server.Access.Models;
using ByteShelfCommon;

namespace ByteShelf.Server.Storage.Services
{
    /// <summary>
    /// A shelf's download and upload links, to see and revoke. Anyone sees and revokes the links they
    /// made; a caller who may write on the shelf, every one of its links.
    /// </summary>
    public interface ILinkService
    {
        /// <summary>The links on the shelf the caller may see that have not run out, newest first.</summary>
        Task<IReadOnlyList<ShelfLink>> ListAsync(ShelfAccess access);

        /// <summary>Ends a link: no request made with it from then on is served.</summary>
        /// <exception cref="Shared.Errors.NotFoundException">The shelf has no such link the caller may see.</exception>
        Task RevokeAsync(ShelfAccess access, Guid linkId);
    }
}
