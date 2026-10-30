using ByteShelf.Server.Shared.Auth;
using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Services;
using ByteShelfCommon;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace ByteShelf.Server.Storage.Controllers
{
    /// <summary>
    /// Files and directories on a shelf. Every endpoint names its entry with exactly one of the
    /// <c>path</c> and <c>id</c> query parameters.
    /// </summary>
    [ApiController]
    [Route("api/shelves/{shelf}")]
    [Authorize(Roles = UserRole.Admin)]
    [ShelfRoute]
    public class EntriesController : ControllerBase
    {
        private readonly IFileService _fileService;
        private readonly IUploadService _uploadService;

        public EntriesController(IFileService fileService, IUploadService uploadService)
        {
            _fileService = fileService;
            _uploadService = uploadService;
        }

        private Shelf Shelf => ShelfRouteAttribute.GetShelf(HttpContext);

        /// <summary>Lists a directory: the root when neither path nor id is given.</summary>
        [HttpGet("entries")]
        [ProducesResponseType(typeof(DirectoryListing), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> List([FromQuery] string? path, [FromQuery] Guid? id)
        {
            EntryReference reference = path == null && id == null ? EntryReference.ForPath(ShelfPath.Root) : EntryReference.From(path, id);
            return Ok(await _fileService.ListDirectoryAsync(Shelf, reference));
        }

        /// <summary>A file's record.</summary>
        [HttpGet("files")]
        [ProducesResponseType(typeof(ShelfFile), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> GetFile([FromQuery] string? path, [FromQuery] Guid? id)
        {
            return Ok(await _fileService.GetFileAsync(Shelf, EntryReference.From(path, id)));
        }

        /// <summary>Moves a file to the trash.</summary>
        [HttpDelete("files")]
        [ProducesResponseType(typeof(TrashEntry), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> DeleteFile([FromQuery] string? path, [FromQuery] Guid? id)
        {
            return Ok(await _fileService.DeleteFileAsync(Shelf, EntryReference.From(path, id)));
        }

        /// <summary>
        /// Streams a file's contents. Supports <c>Range</c>; the ETag is the contents' SHA-256, so
        /// <c>If-None-Match</c> and <c>If-Range</c> work as well. Whatever the stored type, the
        /// browser is told not to sniff it and to run nothing in it, so an uploaded page or image
        /// cannot act with this server's origin.
        /// </summary>
        [HttpGet("content")]
        [HttpHead("content")]
        [ProducesResponseType(typeof(FileStreamResult), 200)]
        [ProducesResponseType(206)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> GetContent([FromQuery] string? path, [FromQuery] Guid? id, [FromQuery] bool download = false)
        {
            FileContent content = await _fileService.GetContentAsync(Shelf, EntryReference.From(path, id));

            Response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";
            Response.Headers[HeaderNames.ContentSecurityPolicy] = "sandbox; default-src 'none'";

            return PhysicalFile(
                content.ObjectPath,
                content.File.ContentType,
                download ? content.File.Name : null,
                content.File.UpdatedAt,
                new EntityTagHeaderValue($"\"{content.File.Sha256}\""),
                enableRangeProcessing: true);
        }

        /// <summary>
        /// Stores the request body as the file at <c>path</c> in one streamed request. Needs
        /// <c>Content-Length</c>, which is reserved against the quota before the body is read; the
        /// <c>Content-Type</c> is stored with the file.
        /// </summary>
        [HttpPut("content")]
        [DisableRequestSizeLimit]
        [ProducesResponseType(typeof(ShelfFile), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 409)]
        [ProducesResponseType(typeof(ErrorResponse), 411)]
        [ProducesResponseType(typeof(ErrorResponse), 507)]
        public async Task<IActionResult> PutContent([FromQuery] string path, [FromQuery] bool overwrite = false, CancellationToken cancellationToken = default)
        {
            long length = Request.ContentLength
                ?? throw new LengthRequiredException("A single upload needs Content-Length. Send content of unknown length as a resumable upload.");

            return Ok(await _uploadService.StoreAsync(Shelf, path, Request.ContentType, length, overwrite, Request.Body, cancellationToken));
        }
    }
}
