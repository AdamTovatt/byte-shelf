using ByteShelf.Server.Shared.Auth;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Services;
using ByteShelfCommon;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ByteShelf.Server.Storage.Controllers
{
    /// <summary>
    /// Creating, moving and deleting a shelf's directories. A new directory's path is in the body; an
    /// existing directory is named by exactly one of the <c>path</c> and <c>id</c> query parameters.
    /// </summary>
    [ApiController]
    [Route("api/shelves/{shelf}/directories")]
    [Authorize(Roles = UserRole.Admin)]
    [ShelfRoute]
    public class DirectoriesController : ControllerBase
    {
        private readonly IDirectoryService _directoryService;

        public DirectoriesController(IDirectoryService directoryService)
        {
            _directoryService = directoryService;
        }

        private Shelf Shelf => ShelfRouteAttribute.GetShelf(HttpContext);

        /// <summary>Creates a directory and any missing directory above it; an existing one is answered as it is.</summary>
        [HttpPost]
        [ProducesResponseType(typeof(ShelfDirectory), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 409)]
        public async Task<IActionResult> Create([FromBody] CreateDirectoryRequest request)
        {
            return Ok(await _directoryService.CreateAsync(Shelf, request.Path));
        }

        /// <summary>Moves or renames a directory with everything in it to the body's <c>toPath</c>.</summary>
        [HttpPost("move")]
        [ProducesResponseType(typeof(ShelfDirectory), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        [ProducesResponseType(typeof(ErrorResponse), 409)]
        public async Task<IActionResult> Move([FromQuery] string? path, [FromQuery] Guid? id, [FromBody] MoveRequest request)
        {
            return Ok(await _directoryService.MoveAsync(Shelf, EntryReference.From(path, id), request.ToPath));
        }

        /// <summary>Moves a directory and everything in it to the trash as one deletion.</summary>
        [HttpDelete]
        [ProducesResponseType(typeof(TrashEntry), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> Delete([FromQuery] string? path, [FromQuery] Guid? id)
        {
            return Ok(await _directoryService.DeleteAsync(Shelf, EntryReference.From(path, id)));
        }
    }
}
