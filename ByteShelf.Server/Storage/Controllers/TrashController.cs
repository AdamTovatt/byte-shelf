using ByteShelf.Server.Access.Controllers;
using ByteShelf.Server.Access.Models;
using ByteShelf.Server.Shared.Auth;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Services;
using ByteShelfCommon;
using Microsoft.AspNetCore.Mvc;

namespace ByteShelf.Server.Storage.Controllers
{
    /// <summary>
    /// What has been deleted from a shelf and can still be restored: listing it, reading what a deletion
    /// removed (on a replica too, which is how one gives back a file purged on its primary), restoring it,
    /// and emptying it.
    /// </summary>
    [ApiController]
    [Route("api/shelves/{shelf}/trash")]
    public class TrashController : ShelfControllerBase
    {
        /// <summary>The deleted contents route where the token may come in a cookie, for a browser's plain navigation.</summary>
        private const string OpenDeletedContentRoute = AuthSetup.CookieTokenPath + "/{shelf}/trash/{deletionId:guid}/content";

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

        /// <summary>
        /// One page of what a deletion removed, by the paths it had, with what the deletion was and the
        /// encrypted folder its files were in. The page's directory is the one it was made in, and its
        /// entries are what it took at <c>under</c> and below it, that entry first: the file deleted, or the
        /// directory and everything under it, by default. 400 for a limit outside 1 to
        /// <see cref="TreeQuery.MaxLimit"/>; 404 when it took nothing at <c>under</c>.
        /// </summary>
        [HttpGet("{deletionId:guid}/tree")]
        [ShelfAccess(ShelfOperation.Read)]
        [ProducesResponseType(typeof(DeletionTree), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> ListDeletion(Guid deletionId, [FromQuery] string? under, [FromQuery] int? limit, [FromQuery] string? after)
        {
            return Ok(await _trashService.ListDeletionAsync(Shelf, deletionId, new TreeQuery { Under = under, Limit = limit, After = after }));
        }

        /// <summary>
        /// Streams the contents of a file a deletion removed, <c>id</c> naming it, as a live file's are sent
        /// (<see cref="FileContentResponse"/>); an encrypted file's are its age bytes. Also served under
        /// <see cref="AuthSetup.CookieTokenPath"/>, as a live file's are, for the web UI's plain navigation.
        /// </summary>
        [HttpGet("{deletionId:guid}/content")]
        [HttpHead("{deletionId:guid}/content")]
        [HttpGet(OpenDeletedContentRoute)]
        [HttpHead(OpenDeletedContentRoute)]
        [ShelfAccess(ShelfOperation.Read)]
        [ProducesResponseType(typeof(FileStreamResult), 200)]
        [ProducesResponseType(206)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> GetDeletedContent(Guid deletionId, [FromQuery] Guid id, [FromQuery] bool download = false)
        {
            FileContent content = await _trashService.GetDeletedContentAsync(Shelf, deletionId, id);
            return FileContentResponse.Send(this, content, download);
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
