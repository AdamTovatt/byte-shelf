using ByteShelf.Server.Access.Controllers;
using ByteShelf.Server.Access.Models;
using ByteShelf.Server.Replication.Controllers;
using ByteShelf.Server.Shared.Auth;
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
    [Route(ShelfRoute)]
    public class EntriesController : ShelfControllerBase
    {
        /// <summary>The contents route where the token may come in a cookie, for a browser's plain navigation.</summary>
        /// <summary>Where a shelf's entries are, <c>{shelf}</c> its name; MCP's instructions name it in their curl commands.</summary>
        internal const string ShelfRoute = "api/shelves/{shelf}";

        private const string OpenContentRoute = AuthSetup.CookieTokenPath + "/{shelf}/content";

        private readonly IDirectoryService _directoryService;
        private readonly IFileService _fileService;
        private readonly IUploadService _uploadService;
        private readonly DownloadLinks _downloadLinks;
        private readonly UploadLinks _uploadLinks;
        private readonly ShelfEncryption _encryption;
        private readonly EntryBatches _entryBatches;

        public EntriesController(
            IDirectoryService directoryService,
            IFileService fileService,
            IUploadService uploadService,
            DownloadLinks downloadLinks,
            UploadLinks uploadLinks,
            ShelfEncryption encryption,
            EntryBatches entryBatches)
        {
            _directoryService = directoryService;
            _fileService = fileService;
            _uploadService = uploadService;
            _downloadLinks = downloadLinks;
            _uploadLinks = uploadLinks;
            _encryption = encryption;
            _entryBatches = entryBatches;
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

        /// <summary>What a file's contents say about it, as read in the background after it was stored.</summary>
        [HttpGet("files/metadata")]
        [ShelfAccess(ShelfOperation.Read)]
        [ProducesResponseType(typeof(FileMetadata), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> GetFileMetadata([FromQuery] string? path, [FromQuery] Guid? id)
        {
            return Ok(await _fileService.GetMetadataAsync(Shelf, EntryReference.From(path, id)));
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
        /// Moves or renames files and directories, as the body (<see cref="MoveEntriesRequest"/>) lists
        /// them: each on its own, in order, so a later one may use a path an earlier one made. Answers one
        /// result per move, in order, each done or refused with its code; nothing is undone. 400 for a
        /// request <see cref="MoveEntriesRequest.Normalize"/> refuses, before anything moves.
        /// </summary>
        [HttpPost("entries/move")]
        [ShelfAccess(ShelfOperation.Write)]
        [ProducesResponseType(typeof(IReadOnlyList<EntryMoveResult>), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        public async Task<IActionResult> MoveEntries([FromBody] MoveEntriesRequest request, CancellationToken cancellationToken)
        {
            return Ok(await _entryBatches.MoveAsync(Shelf, request, cancellationToken));
        }

        /// <summary>
        /// Sets or removes files' descriptions, as the body (<see cref="SetDescriptionsRequest"/>) lists
        /// them, each on its own in order. Answers one result per file, in order, each done or refused
        /// with its code; nothing is undone. 400 for a request <see cref="SetDescriptionsRequest.Normalize"/>
        /// refuses, before anything changes.
        /// </summary>
        [HttpPut("files/descriptions")]
        [ShelfAccess(ShelfOperation.Write)]
        [ProducesResponseType(typeof(IReadOnlyList<DescriptionResult>), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        public async Task<IActionResult> SetDescriptions([FromBody] SetDescriptionsRequest request, CancellationToken cancellationToken)
        {
            return Ok(await _entryBatches.SetDescriptionsAsync(Shelf, request, cancellationToken));
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

        /// <summary>
        /// Finds groups of files with the same contents among the live files under a directory that meet
        /// every criterion given, as <see cref="DuplicateSearch"/> describes, most repeated bytes first,
        /// a page of <c>limit</c> groups at a time. Pass a page's <c>nextCursor</c> as <c>after</c> for
        /// the next.
        /// </summary>
        [HttpGet("duplicates")]
        [ShelfAccess(ShelfOperation.Read)]
        [ProducesResponseType(typeof(DuplicateSearchResults), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> FindDuplicates([FromQuery] DuplicateSearch search)
        {
            return Ok(await _fileService.FindDuplicatesAsync(Shelf, search));
        }

        /// <summary>
        /// Streams a file's contents, as <see cref="FileContentResponse"/> describes. Also served under
        /// <see cref="AuthSetup.CookieTokenPath"/>, where a signed-in browser's token may come in a
        /// cookie, so the web UI opens or saves a file by navigating to it, with no public link.
        /// </summary>
        [HttpGet("content")]
        [HttpHead("content")]
        [HttpGet(OpenContentRoute)]
        [HttpHead(OpenContentRoute)]
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
        /// default, at most 7 days), to share the file, until it is revoked (<see cref="LinksController"/>).
        /// It reads with the caller's access, checked again each time it is used. Making one is a read
        /// (and on a replica, a link of the replica's own); 400 for a lifetime outside those bounds, 409
        /// for an encrypted file.
        /// </summary>
        [HttpPost("download-link")]
        [ShelfAccess(ShelfOperation.Read)]
        [AllowedOnReplica]
        [ProducesResponseType(typeof(DownloadLink), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        [ProducesResponseType(typeof(ErrorResponse), 409)]
        public async Task<IActionResult> CreateDownloadLink([FromQuery] string? path, [FromQuery] Guid? id, [FromQuery] int? expiresInMinutes)
        {
            ShelfFile file = await _fileService.GetFileAsync(Shelf, EntryReference.From(path, id));
            ShelfEncryption.RequireDownloadLinkTarget(file);
            return Ok(await _downloadLinks.CreateAsync(Access, file.Id, expiresInMinutes is int minutes ? TimeSpan.FromMinutes(minutes) : null));
        }

        /// <summary>
        /// A link anyone holding it can store one file through, at the body's <c>path</c>, without a
        /// token, for <c>expiresInMinutes</c> (15 by default, at most 24 hours). It acts as the caller,
        /// whose access is checked now and again when the link is used: one that may overwrite needs
        /// write access. 400 for a request <see cref="CreateUploadLinkRequest.Normalize"/> refuses.
        /// </summary>
        [HttpPost("upload-link")]
        [ShelfAccess(ShelfOperation.Add)]
        [ProducesResponseType(typeof(UploadLink), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 403)]
        [ProducesResponseType(typeof(ErrorResponse), 409)]
        public async Task<IActionResult> CreateUploadLink([FromBody] CreateUploadLinkRequest request)
        {
            Access.RequireStore(request.Overwrite);
            NormalizedUploadLinkRequest normalized = request.Normalize();
            await _encryption.RequireLinkTargetAsync(Shelf.Id, normalized.Path);
            return Ok(await _uploadLinks.CreateAsync(Access, normalized));
        }

        /// <summary>
        /// Stores the request body as the file at <c>path</c> in one streamed request. Needs
        /// <c>Content-Length</c>, which is reserved against the quota before the body is read; the
        /// <c>Content-Type</c> is stored with the file. Replacing a file with <c>overwrite</c> needs
        /// write access. <c>encrypted</c> says the body is an age file encrypted with a password, which
        /// a file under an encrypted directory must be (409 otherwise; 400 for a body that is no age file).
        /// </summary>
        [HttpPut("content")]
        [ShelfAccess(ShelfOperation.Add)]
        [DisableRequestSizeLimit]
        [ProducesResponseType(typeof(ShelfFile), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 403)]
        [ProducesResponseType(typeof(ErrorResponse), 409)]
        [ProducesResponseType(typeof(ErrorResponse), 411)]
        [ProducesResponseType(typeof(ErrorResponse), 507)]
        public async Task<IActionResult> PutContent([FromQuery] string path, [FromQuery] bool overwrite = false, [FromQuery] bool encrypted = false, CancellationToken cancellationToken = default)
        {
            Access.RequireStore(overwrite);

            // The request's body is the contents, so it carries no description.
            StartUploadRequest request = SingleUpload.Of(
                Request, path, overwrite, description: null, encrypted, "A single upload needs Content-Length. Send content of unknown length as a resumable upload.");
            return Ok(await _uploadService.StoreAsync(Shelf, request, Access.Caller.Id, Request.Body, cancellationToken));
        }

        /// <summary>
        /// Swaps the contents of the file <c>id</c> in place for the request body, in one streamed request
        /// with <c>Content-Length</c>: encrypting a plain file (<c>encrypted=true</c>, an age file) or
        /// decrypting an encrypted one, while its contents are still <c>sha256</c> (409 otherwise, or when
        /// the swap would not flip its encryption, or would put a plain file under an encrypted directory).
        /// The file keeps its id, path, type and description; its old contents are removed at once, never
        /// trashed, unless another file holds them. Needs write access.
        /// </summary>
        [HttpPut("content/swap")]
        [ShelfAccess(ShelfOperation.Write)]
        [DisableRequestSizeLimit]
        [ProducesResponseType(typeof(ShelfFile), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 403)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        [ProducesResponseType(typeof(ErrorResponse), 409)]
        [ProducesResponseType(typeof(ErrorResponse), 411)]
        [ProducesResponseType(typeof(ErrorResponse), 507)]
        public async Task<IActionResult> SwapContent([FromQuery] Guid id, [FromQuery] string sha256, [FromQuery] bool encrypted, CancellationToken cancellationToken = default)
        {
            long length = SingleUpload.LengthOf(Request, "A single swap needs Content-Length. Send content of unknown length as a resumable swap.");
            StartContentSwapRequest request = new StartContentSwapRequest { FileId = id, Sha256 = sha256, SizeBytes = length, Encrypted = encrypted };
            return Ok(await _uploadService.SwapAsync(Shelf, request, Access.Caller.Id, Request.Body, cancellationToken));
        }
    }
}
