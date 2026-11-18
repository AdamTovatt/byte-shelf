using ByteShelf.Server.Access.Controllers;
using ByteShelf.Server.Access.Models;
using ByteShelf.Server.Replication.Controllers;
using ByteShelf.Server.Storage.Services;
using ByteShelfCommon;
using Microsoft.AspNetCore.Mvc;

namespace ByteShelf.Server.Storage.Controllers
{
    /// <summary>
    /// The shelf's download and upload links, to see and revoke. Every caller with access sees and
    /// revokes the links it made; one that may write on the shelf, all of them (<see cref="ILinkService"/>).
    /// </summary>
    [ApiController]
    [Route(EntriesController.ShelfRoute + "/links")]
    public class LinksController : ShelfControllerBase
    {
        private readonly ILinkService _linkService;

        public LinksController(ILinkService linkService)
        {
            _linkService = linkService;
        }

        /// <summary>The links the caller may see that have not run out or been revoked, newest first; never their URLs.</summary>
        [HttpGet]
        [ShelfAccess(ShelfOperation.Read)]
        [ProducesResponseType(typeof(IReadOnlyList<ShelfLink>), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> List()
        {
            return Ok(await _linkService.ListAsync(Access));
        }

        /// <summary>
        /// Revokes a link: no request made with it from then on is served, though one already being
        /// served finishes. A replica revokes its own links, which only it has.
        /// 404 when the shelf has no such link the caller may see.
        /// </summary>
        [HttpDelete("{linkId:guid}")]
        [ShelfAccess(ShelfOperation.Read)]
        [AllowedOnReplica]
        [ProducesResponseType(204)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> Revoke(Guid linkId)
        {
            await _linkService.RevokeAsync(Access, linkId);
            return NoContent();
        }
    }
}
