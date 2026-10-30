using ByteShelf.Server.Shared.Auth;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Services;
using ByteShelfCommon;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ByteShelf.Server.Storage.Controllers
{
    /// <summary>Resumable uploads: start with the size, send chunks in order, then complete.</summary>
    [ApiController]
    [Route("api/shelves/{shelf}/uploads")]
    [Authorize(Roles = UserRole.Admin)]
    [ShelfRoute]
    public class UploadsController : ControllerBase
    {
        private readonly IUploadService _uploadService;

        public UploadsController(IUploadService uploadService)
        {
            _uploadService = uploadService;
        }

        private Shelf Shelf => ShelfRouteAttribute.GetShelf(HttpContext);

        /// <summary>Starts an upload, reserving its whole size against the shelf's quota.</summary>
        [HttpPost]
        [ProducesResponseType(typeof(UploadSession), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 409)]
        [ProducesResponseType(typeof(ErrorResponse), 507)]
        public async Task<IActionResult> Start([FromBody] StartUploadRequest request)
        {
            return Ok(await _uploadService.StartAsync(Shelf, request));
        }

        /// <summary>An upload's progress: <c>receivedBytes</c> is where the next chunk starts.</summary>
        [HttpGet("{uploadId:guid}")]
        [ProducesResponseType(typeof(UploadSession), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> Get(Guid uploadId)
        {
            return Ok(await _uploadService.GetAsync(Shelf, uploadId));
        }

        /// <summary>
        /// Appends the request body at <c>offset</c>, which must equal the bytes received so far. A body
        /// over <see cref="UploadLimits.MaxChunkBytes"/> answers 413.
        /// </summary>
        [HttpPut("{uploadId:guid}")]
        [RequestSizeLimit(UploadLimits.MaxChunkBytes)]
        [ProducesResponseType(typeof(UploadSession), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 409)]
        [ProducesResponseType(typeof(ErrorResponse), 413)]
        public async Task<IActionResult> Append(Guid uploadId, [FromQuery] long offset, CancellationToken cancellationToken)
        {
            return Ok(await _uploadService.AppendAsync(Shelf, uploadId, offset, Request.Body, cancellationToken));
        }

        /// <summary>
        /// Stores the file once every declared byte has arrived. When the path was taken meanwhile it
        /// answers 409 and keeps the upload, which can then be aborted.
        /// </summary>
        [HttpPost("{uploadId:guid}/complete")]
        [ProducesResponseType(typeof(ShelfFile), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 409)]
        public async Task<IActionResult> Complete(Guid uploadId, CancellationToken cancellationToken)
        {
            return Ok(await _uploadService.CompleteAsync(Shelf, uploadId, cancellationToken));
        }

        /// <summary>Drops an upload and its bytes, releasing its reservation.</summary>
        [HttpDelete("{uploadId:guid}")]
        [ProducesResponseType(204)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> Abort(Guid uploadId)
        {
            await _uploadService.AbortAsync(Shelf, uploadId);
            return NoContent();
        }
    }
}
