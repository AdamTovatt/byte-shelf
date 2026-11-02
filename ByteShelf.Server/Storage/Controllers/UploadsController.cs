using ByteShelf.Server.Access.Controllers;
using ByteShelf.Server.Access.Models;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Services;
using ByteShelfCommon;
using Microsoft.AspNetCore.Mvc;

namespace ByteShelf.Server.Storage.Controllers
{
    /// <summary>
    /// Resumable uploads: start with the size, send chunks in order, then complete. Only the caller that
    /// started an upload, or one with write access, may use it afterwards.
    /// </summary>
    [ApiController]
    [Route("api/shelves/{shelf}/uploads")]
    public class UploadsController : ShelfControllerBase
    {
        private readonly IUploadService _uploadService;

        public UploadsController(IUploadService uploadService)
        {
            _uploadService = uploadService;
        }

        /// <summary>
        /// Starts an upload, reserving its whole size against the shelf's quota. One that may overwrite a
        /// file needs write access.
        /// </summary>
        [HttpPost]
        [ShelfAccess(ShelfOperation.Add)]
        [ProducesResponseType(typeof(UploadSession), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 403)]
        [ProducesResponseType(typeof(ErrorResponse), 409)]
        [ProducesResponseType(typeof(ErrorResponse), 507)]
        public async Task<IActionResult> Start([FromBody] StartUploadRequest request)
        {
            Access.RequireStore(request.Overwrite);
            return Ok(await _uploadService.StartAsync(Shelf, request, Access.Caller.Id));
        }

        /// <summary>
        /// Starts an upload that swaps a file's contents in place when it completes, keeping its id:
        /// encrypting a plain file or decrypting an encrypted one, while its contents are still
        /// <c>sha256</c> (409 otherwise). Its old contents are removed at once, never trashed, unless
        /// another file holds them. Needs write access.
        /// </summary>
        [HttpPost("swap")]
        [ShelfAccess(ShelfOperation.Write)]
        [ProducesResponseType(typeof(UploadSession), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 403)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        [ProducesResponseType(typeof(ErrorResponse), 409)]
        [ProducesResponseType(typeof(ErrorResponse), 507)]
        public async Task<IActionResult> StartSwap([FromBody] StartContentSwapRequest request)
        {
            return Ok(await _uploadService.StartSwapAsync(Shelf, request, Access.Caller.Id));
        }

        /// <summary>An upload's progress: <c>receivedBytes</c> is where the next chunk starts.</summary>
        [HttpGet("{uploadId:guid}")]
        [ShelfAccess(ShelfOperation.Add)]
        [ProducesResponseType(typeof(UploadSession), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 403)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> Get(Guid uploadId)
        {
            return Ok(_uploadService.ToSession(await GetPermittedAsync(uploadId)));
        }

        /// <summary>
        /// Appends the request body at <c>offset</c>, which must equal the bytes received so far. A body
        /// over <see cref="UploadLimits.MaxChunkBytes"/> answers 413.
        /// </summary>
        [HttpPut("{uploadId:guid}")]
        [ShelfAccess(ShelfOperation.Add)]
        [RequestSizeLimit(UploadLimits.MaxChunkBytes)]
        [ProducesResponseType(typeof(UploadSession), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 403)]
        [ProducesResponseType(typeof(ErrorResponse), 409)]
        [ProducesResponseType(typeof(ErrorResponse), 413)]
        public async Task<IActionResult> Append(Guid uploadId, [FromQuery] long offset, CancellationToken cancellationToken)
        {
            await GetPermittedAsync(uploadId);
            return Ok(await _uploadService.AppendAsync(Shelf, uploadId, offset, Request.Body, cancellationToken));
        }

        /// <summary>
        /// Stores the file once every declared byte has arrived. When the path was taken meanwhile it
        /// answers 409 and keeps the upload, which can then be aborted.
        /// </summary>
        [HttpPost("{uploadId:guid}/complete")]
        [ShelfAccess(ShelfOperation.Add)]
        [ProducesResponseType(typeof(ShelfFile), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 403)]
        [ProducesResponseType(typeof(ErrorResponse), 409)]
        public async Task<IActionResult> Complete(Guid uploadId, CancellationToken cancellationToken)
        {
            await GetPermittedAsync(uploadId);
            return Ok(await _uploadService.CompleteAsync(Shelf, uploadId, cancellationToken));
        }

        /// <summary>Drops an upload and its bytes, releasing its reservation.</summary>
        [HttpDelete("{uploadId:guid}")]
        [ShelfAccess(ShelfOperation.Add)]
        [ProducesResponseType(204)]
        [ProducesResponseType(typeof(ErrorResponse), 403)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> Abort(Guid uploadId)
        {
            await GetPermittedAsync(uploadId);
            await _uploadService.AbortAsync(Shelf, uploadId);
            return NoContent();
        }

        /// <summary>
        /// The upload, if the caller may use it. Who started an upload and whether it may overwrite never
        /// change, so the check holds for the rest of the request.
        /// </summary>
        private async Task<UploadRecord> GetPermittedAsync(Guid uploadId)
        {
            UploadRecord upload = await _uploadService.GetAsync(Shelf, uploadId);
            Access.RequireUpload(upload);
            return upload;
        }
    }
}
