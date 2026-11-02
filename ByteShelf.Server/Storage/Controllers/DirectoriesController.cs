using ByteShelf.Server.Access.Controllers;
using ByteShelf.Server.Access.Models;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Services;
using ByteShelfCommon;
using Microsoft.AspNetCore.Mvc;

namespace ByteShelf.Server.Storage.Controllers
{
    /// <summary>
    /// Creating, moving and deleting a shelf's directories. A new directory's path is in the body; an
    /// existing directory is named by exactly one of the <c>path</c> and <c>id</c> query parameters.
    /// </summary>
    [ApiController]
    [Route("api/shelves/{shelf}/directories")]
    public class DirectoriesController : ShelfControllerBase
    {
        private readonly IDirectoryService _directoryService;

        public DirectoriesController(IDirectoryService directoryService)
        {
            _directoryService = directoryService;
        }

        /// <summary>Creates a directory and any missing directory above it; an existing one is answered as it is.</summary>
        [HttpPost]
        [ShelfAccess(ShelfOperation.Add)]
        [ProducesResponseType(typeof(ShelfDirectory), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 409)]
        public async Task<IActionResult> Create([FromBody] CreateDirectoryRequest request)
        {
            return Ok(await _directoryService.CreateAsync(Shelf, request.Path));
        }

        /// <summary>Moves or renames a directory with everything in it to the body's <c>toPath</c>.</summary>
        [HttpPost("move")]
        [ShelfAccess(ShelfOperation.Write)]
        [ProducesResponseType(typeof(ShelfDirectory), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        [ProducesResponseType(typeof(ErrorResponse), 409)]
        public async Task<IActionResult> Move([FromQuery] string? path, [FromQuery] Guid? id, [FromBody] MoveRequest request)
        {
            return Ok(await _directoryService.MoveAsync(Shelf, EntryReference.From(path, id), request.ToPath));
        }

        /// <summary>
        /// The encrypted directory a file stored at <c>path</c> (a file's or a directory's, existing or
        /// not) would be under, with its check; <c>encryptedBy</c> is null when there is none.
        /// </summary>
        [HttpGet("encryption")]
        [ShelfAccess(ShelfOperation.Read)]
        [ProducesResponseType(typeof(PathEncryption), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        public async Task<IActionResult> GetEncryption([FromQuery] string path)
        {
            return Ok(await _directoryService.GetEncryptionAsync(Shelf, path));
        }

        /// <summary>
        /// Encrypts a directory: from now on every file stored under it must be encrypted with the
        /// password the body's <c>check</c> was. Its files are not touched; a client encrypts them. 409
        /// when it is encrypted already, or inside or above an encrypted directory; 400 for a check that is
        /// not base64 of an age file of at most 1024 bytes.
        /// </summary>
        [HttpPut("encryption")]
        [ShelfAccess(ShelfOperation.Write)]
        [ProducesResponseType(typeof(ShelfDirectory), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        [ProducesResponseType(typeof(ErrorResponse), 409)]
        public async Task<IActionResult> Encrypt([FromQuery] string? path, [FromQuery] Guid? id, [FromBody] SetFolderEncryptionRequest request)
        {
            return Ok(await _directoryService.EncryptAsync(Shelf, EntryReference.From(path, id), request.Check));
        }

        /// <summary>
        /// Stops encrypting a directory: files stored under it may be plain again. The files there stay
        /// encrypted until a client decrypts them. 409 when it is not encrypted itself.
        /// </summary>
        [HttpDelete("encryption")]
        [ShelfAccess(ShelfOperation.Write)]
        [ProducesResponseType(typeof(ShelfDirectory), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        [ProducesResponseType(typeof(ErrorResponse), 409)]
        public async Task<IActionResult> RemoveEncryption([FromQuery] string? path, [FromQuery] Guid? id)
        {
            return Ok(await _directoryService.RemoveEncryptionAsync(Shelf, EntryReference.From(path, id)));
        }

        /// <summary>Moves a directory and everything in it to the trash as one deletion.</summary>
        [HttpDelete]
        [ShelfAccess(ShelfOperation.Write)]
        [ProducesResponseType(typeof(TrashEntry), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> Delete([FromQuery] string? path, [FromQuery] Guid? id)
        {
            return Ok(await _directoryService.DeleteAsync(Shelf, EntryReference.From(path, id)));
        }
    }
}
