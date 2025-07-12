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
        private readonly IFileStorageService _fileStorageService;
        private readonly ITenantConfigurationService _tenantConfigurationService;

        /// <summary>
        /// Initializes a new instance of the <see cref="ChunksController"/> class.
        /// </summary>
        /// <param name="fileStorageService">The file storage service for chunk operations.</param>
        /// <param name="tenantConfigurationService">The tenant configuration service for access validation.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="fileStorageService"/> or <paramref name="tenantConfigurationService"/> is null.</exception>
        public ChunksController(IFileStorageService fileStorageService, ITenantConfigurationService tenantConfigurationService)
        {
            _fileStorageService = fileStorageService ?? throw new ArgumentNullException(nameof(fileStorageService));
            _tenantConfigurationService = tenantConfigurationService ?? throw new ArgumentNullException(nameof(tenantConfigurationService));
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
                Stream chunkStream = await _fileStorageService.GetChunkAsync(tenantId, chunkId, cancellationToken);
                return File(chunkStream, "application/octet-stream");
            }
            catch (FileNotFoundException)
            {
                return NotFound();
            }
        }

        /// <summary>
        /// Retrieves a chunk by its ID for a specific tenant.
        /// </summary>
        /// <param name="targetTenantId">The ID of the tenant whose chunk to retrieve.</param>
        /// <param name="chunkId">The unique identifier of the chunk to retrieve.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>The chunk data as a binary stream.</returns>
        /// <response code="200">Returns the chunk data as application/octet-stream.</response>
        /// <response code="401">If the API key is invalid or missing.</response>
        /// <response code="403">If the authenticated tenant does not have access to the specified tenant.</response>
        /// <response code="404">If the chunk with the specified ID does not exist or the specified tenant does not exist.</response>
        /// <remarks>
        /// This endpoint allows a parent tenant to access chunks from its subtenants.
        /// The authenticated tenant must have access to the specified tenant (either be the same tenant or a parent).
        /// This endpoint returns the raw binary data of the chunk.
        /// The response content type is "application/octet-stream".
        /// </remarks>
        [HttpGet("{targetTenantId}/{chunkId}")]
        [ProducesResponseType(typeof(FileStreamResult), 200)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> GetChunkForTenant(string targetTenantId, Guid chunkId, CancellationToken cancellationToken)
        {
            string authenticatedTenantId = HttpContext.GetTenantId();

            // Validate access to the target tenant
            if (!_tenantConfigurationService.HasAccessToTenant(authenticatedTenantId, targetTenantId))
            {
                return Unauthorized();
            }

            // Check if target tenant exists
            if (_tenantConfigurationService.GetTenant(targetTenantId) == null)
            {
                return NotFound();
            }

            try
            {
                Stream chunkStream = await _fileStorageService.GetChunkAsync(targetTenantId, chunkId, cancellationToken);
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
                Guid savedChunkId = await _fileStorageService.SaveChunkAsync(tenantId, chunkId, Request.Body, cancellationToken);
                return Ok(new { ChunkId = savedChunkId });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("exceed their storage quota"))
            {
                return StatusCode(413, new { error = "Storage quota exceeded", message = ex.Message });
            }
        }

        /// <summary>
        /// Uploads a new chunk with the specified ID for a specific tenant.
        /// </summary>
        /// <param name="targetTenantId">The ID of the tenant for which to upload the chunk.</param>
        /// <param name="chunkId">The unique identifier for the chunk.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>The ID of the saved chunk.</returns>
        /// <response code="200">Returns the chunk ID that was saved.</response>
        /// <response code="400">If no content is provided in the request body.</response>
        /// <response code="401">If the API key is invalid or missing.</response>
        /// <response code="403">If the authenticated tenant does not have access to the specified tenant.</response>
        /// <response code="404">If the specified tenant does not exist.</response>
        /// <response code="413">If the tenant would exceed their storage quota.</response>
        /// <remarks>
        /// This endpoint allows a parent tenant to upload chunks for its subtenants.
        /// The authenticated tenant must have access to the specified tenant (either be the same tenant or a parent).
        /// This endpoint accepts binary data in the request body and stores it as a chunk.
        /// If a chunk with the same ID already exists, it will be overwritten.
        /// The chunk data should be sent as the raw request body without any encoding.
        /// The chunk will be stored for the specified tenant and quota limits will be enforced.
        /// </remarks>
        [HttpPut("{targetTenantId}/{chunkId}")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(404)]
        [ProducesResponseType(413)]
        public async Task<IActionResult> UploadChunkForTenant(string targetTenantId, Guid chunkId, CancellationToken cancellationToken)
        {
            if (Request.Body == null)
                return BadRequest("No content provided");

            string authenticatedTenantId = HttpContext.GetTenantId();

            // Validate access to the target tenant
            if (!_tenantConfigurationService.HasAccessToTenant(authenticatedTenantId, targetTenantId))
            {
                return Unauthorized();
            }

            // Check if target tenant exists
            if (_tenantConfigurationService.GetTenant(targetTenantId) == null)
            {
                return NotFound();
            }

            try
            {
                Guid savedChunkId = await _fileStorageService.SaveChunkAsync(targetTenantId, chunkId, Request.Body, cancellationToken);
                return Ok(new { ChunkId = savedChunkId });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("exceed their storage quota"))
            {
                return StatusCode(413, new { error = "Storage quota exceeded", message = ex.Message });
            }
        }
    }
}