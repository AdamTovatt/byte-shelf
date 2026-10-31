using ByteShelf.Server.Access.Controllers;
using ByteShelf.Server.Access.Models;
using ByteShelf.Server.Storage.Services;
using ByteShelfCommon;
using Microsoft.AspNetCore.Mvc;

namespace ByteShelf.Server.Storage.Controllers
{
    /// <summary>What has been deleted from a shelf and can still be restored, and emptying it.</summary>
    [ApiController]
    [Route("api/shelves/{shelf}/trash")]
    public class TrashController : ShelfControllerBase
    {
        private readonly ITrashService _trashService;

        public TrashController(ITrashService trashService)
        {
            _trashService = trashService;
        }

        /// <summary>Every deletion still inside the restore window, newest first.</summary>
        [HttpGet]
        [ShelfAccess(ShelfOperation.Read)]
        [ProducesResponseType(typeof(IReadOnlyList<TrashEntry>), 200)]
        public async Task<IActionResult> List()
        {
            return Ok(await _trashService.GetTrashAsync(Shelf));
        }

        /// <summary>Puts back what one deletion removed, where it was.</summary>
        [HttpPost("{deletionId:guid}/restore")]
        [ShelfAccess(ShelfOperation.Write)]
        [ProducesResponseType(204)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        [ProducesResponseType(typeof(ErrorResponse), 409)]
        public async Task<IActionResult> Restore(Guid deletionId)
        {
            await _trashService.RestoreAsync(Shelf, deletionId);
            return NoContent();
        }

        /// <summary>
        /// Empties the trash: everything in it is removed for good now rather than when its window
        /// ends, and can no longer be restored.
        /// </summary>
        [HttpDelete]
        [ShelfAccess(ShelfOperation.Write)]
        [ProducesResponseType(204)]
        public async Task<IActionResult> EmptyTrash(CancellationToken cancellationToken)
        {
            await _trashService.EmptyAsync(Shelf, cancellationToken);
            return NoContent();
        }
    }
}
