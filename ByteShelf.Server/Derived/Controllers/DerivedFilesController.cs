using ByteShelf.Server.Access.Controllers;
using ByteShelf.Server.Access.Models;
using ByteShelf.Server.Derived.Services;
using ByteShelf.Server.Shared.Auth;
using ByteShelf.Server.Storage.Controllers;
using ByteShelfCommon;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ByteShelf.Server.Derived.Controllers
{
    /// <summary>
    /// What plugins made from a file's contents, read by whoever may read the file. Also served under
    /// <see cref="AuthSetup.CookieTokenPath"/>, as contents are, so a browser loads one as an image's source.
    /// </summary>
    [ApiController]
    public class DerivedFilesController : ShelfControllerBase
    {
        private const string FileRoute = EntriesController.ShelfRoute + "/files/derived";
        private const string OpenFileRoute = EntriesController.OpenShelfRoute + "/files/derived";
        private const string DeletedFileRoute = TrashController.ShelfTrashRoute + "/{deletionId:guid}/derived";
        private const string OpenDeletedFileRoute = EntriesController.OpenShelfRoute + "/trash/{deletionId:guid}/derived";

        private readonly IDerivedFileService _derivedFiles;

        public DerivedFilesController(IDerivedFileService derivedFiles)
        {
            _derivedFiles = derivedFiles;
        }

        /// <summary>
        /// The derived file of <c>kind</c> of a file, named by <c>path</c> or <c>id</c>. One that is missing is made
        /// and waited for, at most 10 seconds: 503 with <c>Retry-After</c> then, while it is made; a missing stream is
        /// not made, but in the background, and is a 503 at once. 404 when no plugin makes it, when it could not be
        /// made, and for an encrypted file.
        /// </summary>
        [HttpGet(FileRoute)]
        [HttpHead(FileRoute)]
        [HttpGet(OpenFileRoute)]
        [HttpHead(OpenFileRoute)]
        [ShelfAccess(ShelfOperation.Read)]
        [ProducesResponseType(typeof(FileStreamResult), 200)]
        [ProducesResponseType(206)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        [ProducesResponseType(typeof(ErrorResponse), 503)]
        public async Task<IActionResult> Get([FromQuery] string? path, [FromQuery] Guid? id, [FromQuery, BindRequired] DerivedKind kind, CancellationToken cancellationToken)
        {
            return DerivedFileResponse.Send(this, await _derivedFiles.GetAsync(Shelf, EntryReference.From(path, id), kind, cancellationToken));
        }

        /// <summary>As of a live file, of one a deletion removed, <c>id</c> naming it.</summary>
        [HttpGet(DeletedFileRoute)]
        [HttpHead(DeletedFileRoute)]
        [HttpGet(OpenDeletedFileRoute)]
        [HttpHead(OpenDeletedFileRoute)]
        [ShelfAccess(ShelfOperation.Read)]
        [ProducesResponseType(typeof(FileStreamResult), 200)]
        [ProducesResponseType(206)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        [ProducesResponseType(typeof(ErrorResponse), 503)]
        public async Task<IActionResult> GetDeleted(Guid deletionId, [FromQuery] Guid id, [FromQuery, BindRequired] DerivedKind kind, CancellationToken cancellationToken)
        {
            return DerivedFileResponse.Send(this, await _derivedFiles.GetDeletedAsync(Shelf, deletionId, id, kind, cancellationToken));
        }
    }
}
