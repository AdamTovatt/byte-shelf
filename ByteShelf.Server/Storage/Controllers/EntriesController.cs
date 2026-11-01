using ByteShelf.Server.Access.Controllers;
using ByteShelf.Server.Access.Models;
using ByteShelf.Server.Replication.Controllers;
using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Services;
using ByteShelfCommon;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace ByteShelf.Server.Storage.Controllers
{
    /// <summary>
    /// Listing a shelf's directories, and its files with their contents. Every endpoint names its entry
    /// with exactly one of the <c>path</c> and <c>id</c> query parameters; a listing without either is
    /// of the root.
    /// </summary>
    [ApiController]
    [Route("api/shelves/{shelf}")]
    public class EntriesController : ShelfControllerBase
    {
        private readonly IDirectoryService _directoryService;
        private readonly IFileService _fileService;
        private readonly IUploadService _uploadService;
        private readonly DownloadLinks _downloadLinks;

        public EntriesController(IDirectoryService directoryService, IFileService fileService, IUploadService uploadService, DownloadLinks downloadLinks)
        {
            _directoryService = directoryService;
            _fileService = fileService;
            _uploadService = uploadService;
            _downloadLinks = downloadLinks;
        }

        /// <summary>Lists a directory: the root when neither path nor id is given.</summary>
        [HttpGet("entries")]
        [ShelfAccess(ShelfOperation.Read)]
        [ProducesResponseType(typeof(DirectoryListing), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> List([FromQuery] string? path, [FromQuery] Guid? id)
        {
            EntryReference reference = path == null && id == null ? EntryReference.ForPath(ShelfPath.Root) : EntryReference.From(path, id);
            return Ok(await _directoryService.ListAsync(Shelf, reference));
        }

        /// <summary>
        /// One page of everything under a directory (<c>under</c>, the root by default), at any depth or
        /// down to <c>depth</c> levels, by path; pass the page's <c>nextCursor</c> as <c>after</c> for the next.
        /// </summary>
        [HttpGet("tree")]
        [ShelfAccess(ShelfOperation.Read)]
        [ProducesResponseType(typeof(DirectoryTree), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> ListTree([FromQuery] TreeQuery query)
        {
            return Ok(await _directoryService.ListTreeAsync(Shelf, query));
        }

        /// <summary>A file's record.</summary>
        [HttpGet("files")]
        [ShelfAccess(ShelfOperation.Read)]
        [ProducesResponseType(typeof(ShelfFile), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> GetFile([FromQuery] string? path, [FromQuery] Guid? id)
        {
            return Ok(await _fileService.GetFileAsync(Shelf, EntryReference.From(path, id)));
        }

        /// <summary>Moves a file to the trash.</summary>
        [HttpDelete("files")]
        [ShelfAccess(ShelfOperation.Write)]
        [ProducesResponseType(typeof(TrashEntry), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> DeleteFile([FromQuery] string? path, [FromQuery] Guid? id)
        {
            return Ok(await _fileService.DeleteFileAsync(Shelf, EntryReference.From(path, id)));
        }

        /// <summary>
        /// Moves or renames a file to the body's <c>toPath</c>; the stored contents stay where they are.
        /// </summary>
        [HttpPost("files/move")]
        [ShelfAccess(ShelfOperation.Write)]
        [ProducesResponseType(typeof(ShelfFile), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        [ProducesResponseType(typeof(ErrorResponse), 409)]
        public async Task<IActionResult> MoveFile([FromQuery] string? path, [FromQuery] Guid? id, [FromBody] MoveRequest request)
        {
            return Ok(await _fileService.MoveFileAsync(Shelf, EntryReference.From(path, id), request.ToPath));
        }

        /// <summary>Sets a file's description, or removes it with null or blank text.</summary>
        [HttpPut("files/description")]
        [ShelfAccess(ShelfOperation.Write)]
        [ProducesResponseType(typeof(ShelfFile), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> SetDescription([FromQuery] string? path, [FromQuery] Guid? id, [FromBody] SetDescriptionRequest request)
        {
            return Ok(await _fileService.SetDescriptionAsync(Shelf, EntryReference.From(path, id), request.Description));
        }

        /// <summary>
        /// Finds live files under a directory, at any depth, that match every criterion given, a page at
        /// a time by path; <see cref="FileSearch"/> describes each. Pass a page's <c>nextCursor</c> as
        /// <c>after</c> for the next.
        /// </summary>
        [HttpGet("search")]
        [ShelfAccess(ShelfOperation.Read)]
        [ProducesResponseType(typeof(FileSearchResults), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> Search([FromQuery] FileSearch search)
        {
            return Ok(await _fileService.SearchAsync(Shelf, search));
        }

        /// <summary>Streams a file's contents, as <see cref="FileContentResponse"/> describes.</summary>
        [HttpGet("content")]
        [HttpHead("content")]
        [ShelfAccess(ShelfOperation.Read)]
        [ProducesResponseType(typeof(FileStreamResult), 200)]
        [ProducesResponseType(206)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> GetContent([FromQuery] string? path, [FromQuery] Guid? id, [FromQuery] bool download = false)
        {
            FileContent content = await _fileService.GetContentAsync(Shelf, EntryReference.From(path, id));
            return FileContentResponse.Send(this, content, download);
        }

        /// <summary>
        /// A link to a file's contents that needs no token for <c>expiresInMinutes</c> (24 hours by
        /// default, at most 7 days): for a browser to open or save, which a plain link cannot send a token
        /// with, or to share the file. Making one is a read; 400 for a lifetime outside those bounds.
        /// </summary>
        [HttpPost("download-link")]
        [ShelfAccess(ShelfOperation.Read)]
        [AllowedOnReplica]
        [ProducesResponseType(typeof(DownloadLink), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> CreateDownloadLink([FromQuery] string? path, [FromQuery] Guid? id, [FromQuery] int? expiresInMinutes)
        {
            ShelfFile file = await _fileService.GetFileAsync(Shelf, EntryReference.From(path, id));
            return Ok(_downloadLinks.Create(Shelf.Id, file.Id, expiresInMinutes is int minutes ? TimeSpan.FromMinutes(minutes) : null));
        }

        /// <summary>
        /// Stores the request body as the file at <c>path</c> in one streamed request. Needs
        /// <c>Content-Length</c>, which is reserved against the quota before the body is read; the
        /// <c>Content-Type</c> is stored with the file. Replacing a file with <c>overwrite</c> needs
        /// write access.
        /// </summary>
        [HttpPut("content")]
        [ShelfAccess(ShelfOperation.Add)]
        [DisableRequestSizeLimit]
        [ProducesResponseType(typeof(ShelfFile), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 403)]
        [ProducesResponseType(typeof(ErrorResponse), 409)]
        [ProducesResponseType(typeof(ErrorResponse), 411)]
        [ProducesResponseType(typeof(ErrorResponse), 507)]
        public async Task<IActionResult> PutContent([FromQuery] string path, [FromQuery] bool overwrite = false, CancellationToken cancellationToken = default)
        {
            Access.RequireStore(overwrite);

            long length = Request.ContentLength
                ?? throw new LengthRequiredException("A single upload needs Content-Length. Send content of unknown length as a resumable upload.");

            return Ok(await _uploadService.StoreAsync(Shelf, path, Request.ContentType, length, overwrite, Access.Caller.Id, Request.Body, cancellationToken));
        }
    }
}
