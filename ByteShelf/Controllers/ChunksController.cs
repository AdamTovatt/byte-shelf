using ByteShelf.Extensions;
using ByteShelf.Services;
using Microsoft.AspNetCore.Mvc;

namespace ByteShelf.Controllers
{
    /// <summary>
    /// Controller for managing file chunks with tenant isolation.
    /// </summary>
    /// <remarks>
    /// This controller provides REST API endpoints for chunk operations including:
    /// - Retrieving individual chunks by ID, scoped to the authenticated tenant
    /// - Uploading new chunks for the authenticated tenant
    /// Chunks are the binary data pieces that make up the actual file content.
    /// All endpoints require API key authentication and are scoped to the authenticated tenant.
    /// </remarks>
    [ApiController]
    [Route("api/[controller]")]
    public class ChunksController : ControllerBase
    {
        private readonly ITenantFileStorageService _tenantFileStorageService;

        /// <summary>
        /// Initializes a new instance of the <see cref="ChunksController"/> class.
        /// </summary>
        /// <param name="tenantFileStorageService">The tenant-aware file storage service for chunk operations.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="tenantFileStorageService"/> is null.</exception>
        public ChunksController(ITenantFileStorageService tenantFileStorageService)
        {
            _tenantFileStorageService = tenantFileStorageService ?? throw new ArgumentNullException(nameof(tenantFileStorageService));
        }

        /// <summary>
        /// Retrieves a chunk by its ID, scoped to the authenticated tenant.
        /// </summary>
        /// <param name="chunkId">The unique identifier of the chunk to retrieve.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>The chunk data as a binary stream.</returns>
        /// <response code="200">Returns the chunk data as application/octet-stream.</response>
        /// <response code="404">If the chunk with the specified ID does not exist or does not belong to the tenant.</response>
        /// <response code="401">If the API key is invalid or missing.</response>
        /// <remarks>
        /// This endpoint returns the raw binary data of the chunk.
        /// The response content type is "application/octet-stream".
        /// Only chunks belonging to the authenticated tenant can be accessed.
        /// </remarks>
        [HttpGet("{chunkId}")]
        [ProducesResponseType(typeof(FileStreamResult), 200)]
        [ProducesResponseType(404)]
        [ProducesResponseType(401)]
        public async Task<IActionResult> GetChunk(Guid chunkId, CancellationToken cancellationToken)
        {
            try
            {
                string tenantId = HttpContext.GetTenantId();
                Stream chunkStream = await _tenantFileStorageService.GetChunkAsync(tenantId, chunkId, cancellationToken);
                return File(chunkStream, "application/octet-stream");
            }
            catch (FileNotFoundException)
            {
                return NotFound();
            }
        }

        /// <summary>
        /// Uploads a new chunk with the specified ID for the authenticated tenant.
        /// </summary>
        /// <param name="chunkId">The unique identifier for the chunk.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>The ID of the saved chunk.</returns>
        /// <response code="200">Returns the chunk ID that was saved.</response>
        /// <response code="400">If no content is provided in the request body.</response>
        /// <response code="401">If the API key is invalid or missing.</response>
        /// <response code="413">If the tenant would exceed their storage quota.</response>
        /// <remarks>
        /// This endpoint accepts binary data in the request body and stores it as a chunk.
        /// If a chunk with the same ID already exists, it will be overwritten.
        /// The chunk data should be sent as the raw request body without any encoding.
        /// The chunk will be stored for the authenticated tenant and quota limits will be enforced.
        /// </remarks>
        [HttpPut("{chunkId}")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(413)]
        public async Task<IActionResult> UploadChunk(Guid chunkId, CancellationToken cancellationToken)
        {
            if (Request.Body == null)
                return BadRequest("No content provided");

            try
            {
                string tenantId = HttpContext.GetTenantId();
                Guid savedChunkId = await _tenantFileStorageService.SaveChunkAsync(tenantId, chunkId, Request.Body, cancellationToken);
                return Ok(new { ChunkId = savedChunkId });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("exceed their storage quota"))
            {
                return StatusCode(413, new { error = "Storage quota exceeded", message = ex.Message });
            }
        }
    }
}