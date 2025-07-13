using ByteShelf.Extensions;
using ByteShelf.Services;
using ByteShelfCommon;
using Microsoft.AspNetCore.Mvc;

namespace ByteShelf.Controllers
{
    /// <summary>
    /// Controller for managing file metadata and operations with tenant isolation.
    /// </summary>
    /// <remarks>
    /// This controller provides REST API endpoints for file operations including:
    /// - Listing all files for a tenant
    /// - Retrieving file metadata for a tenant
    /// - Creating file metadata for a tenant
    /// - Deleting files and their associated chunks for a tenant
    /// All endpoints require API key authentication and are scoped to the authenticated tenant.
    /// </remarks>
    [ApiController]
    [Route("api/[controller]")]
    public class FilesController : ControllerBase
    {
        private readonly IFileStorageService _fileStorageService;
        private readonly ITenantConfigurationService _tenantConfigurationService;

        /// <summary>
        /// Initializes a new instance of the <see cref="FilesController"/> class.
        /// </summary>
        /// <param name="fileStorageService">The file storage service for file operations.</param>
        /// <param name="tenantConfigurationService">The tenant configuration service for access validation.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="fileStorageService"/> or <paramref name="tenantConfigurationService"/> is null.</exception>
        public FilesController(IFileStorageService fileStorageService, ITenantConfigurationService tenantConfigurationService)
        {
            _fileStorageService = fileStorageService ?? throw new ArgumentNullException(nameof(fileStorageService));
            _tenantConfigurationService = tenantConfigurationService ?? throw new ArgumentNullException(nameof(tenantConfigurationService));
        }

        /// <summary>
        /// Retrieves metadata for all files belonging to the authenticated tenant.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A collection of file metadata for all files belonging to the tenant.</returns>
        /// <response code="200">Returns the list of file metadata.</response>
        /// <response code="401">If the API key is invalid or missing.</response>
        /// <remarks>
        /// This endpoint returns metadata only, not the actual file content.
        /// Use the chunks endpoints to retrieve file content.
        /// All files returned belong to the authenticated tenant.
        /// </remarks>
        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<ShelfFileMetadata>), 200)]
        [ProducesResponseType(401)]
        public async Task<ActionResult<IEnumerable<ShelfFileMetadata>>> GetFiles(CancellationToken cancellationToken)
        {
            string tenantId = HttpContext.GetTenantId();
            IEnumerable<ShelfFileMetadata> files = await _fileStorageService.GetFilesAsync(tenantId, cancellationToken);
            return Ok(files);
        }

        /// <summary>
        /// Retrieves metadata for all files belonging to a specific tenant.
        /// </summary>
        /// <param name="targetTenantId">The ID of the tenant whose files to retrieve.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A collection of file metadata for all files belonging to the specified tenant.</returns>
        /// <response code="200">Returns the list of file metadata.</response>
        /// <response code="401">If the API key is invalid or missing.</response>
        /// <response code="403">If the authenticated tenant does not have access to the specified tenant.</response>
        /// <response code="404">If the specified tenant does not exist.</response>
        /// <remarks>
        /// This endpoint allows a parent tenant to access files from its subtenants.
        /// The authenticated tenant must have access to the specified tenant (either be the same tenant or a parent).
        /// This endpoint returns metadata only, not the actual file content.
        /// Use the chunks endpoints to retrieve file content.
        /// </remarks>
        [HttpGet("{targetTenantId}")]
        [ProducesResponseType(typeof(IEnumerable<ShelfFileMetadata>), 200)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(404)]
        public async Task<ActionResult<IEnumerable<ShelfFileMetadata>>> GetFilesForTenant(string targetTenantId, CancellationToken cancellationToken)
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

            IEnumerable<ShelfFileMetadata> files = await _fileStorageService.GetFilesAsync(targetTenantId, cancellationToken);
            return Ok(files);
        }

        /// <summary>
        /// Retrieves metadata for a specific file by its ID, scoped to the authenticated tenant.
        /// </summary>
        /// <param name="fileId">The unique identifier of the file.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>The file metadata if found and belongs to the tenant.</returns>
        /// <response code="200">Returns the file metadata.</response>
        /// <response code="404">If the file with the specified ID does not exist or does not belong to the tenant.</response>
        /// <response code="401">If the API key is invalid or missing.</response>
        /// <remarks>
        /// This endpoint returns metadata only, not the actual file content.
        /// Use the chunks endpoints to retrieve file content.
        /// Only files belonging to the authenticated tenant can be accessed.
        /// </remarks>
        [HttpGet("{fileId}/metadata")]
        [ProducesResponseType(typeof(ShelfFileMetadata), 200)]
        [ProducesResponseType(404)]
        [ProducesResponseType(401)]
        public async Task<ActionResult<ShelfFileMetadata>> GetFileMetadata(Guid fileId, CancellationToken cancellationToken)
        {
            string tenantId = HttpContext.GetTenantId();
            ShelfFileMetadata? metadata = await _fileStorageService.GetFileMetadataAsync(tenantId, fileId, cancellationToken);

            if (metadata == null)
                return NotFound();

            return Ok(metadata);
        }

        /// <summary>
        /// Retrieves metadata for a specific file by its ID, scoped to a specific tenant.
        /// </summary>
        /// <param name="targetTenantId">The ID of the tenant whose file to retrieve.</param>
        /// <param name="fileId">The unique identifier of the file.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>The file metadata if found and belongs to the specified tenant.</returns>
        /// <response code="200">Returns the file metadata.</response>
        /// <response code="401">If the API key is invalid or missing.</response>
        /// <response code="403">If the authenticated tenant does not have access to the specified tenant.</response>
        /// <response code="404">If the file with the specified ID does not exist or the specified tenant does not exist.</response>
        /// <remarks>
        /// This endpoint allows a parent tenant to access file metadata from its subtenants.
        /// The authenticated tenant must have access to the specified tenant (either be the same tenant or a parent).
        /// This endpoint returns metadata only, not the actual file content.
        /// Use the chunks endpoints to retrieve file content.
        /// </remarks>
        [HttpGet("{targetTenantId}/{fileId}/metadata")]
        [ProducesResponseType(typeof(ShelfFileMetadata), 200)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(404)]
        public async Task<ActionResult<ShelfFileMetadata>> GetFileMetadataForTenant(string targetTenantId, Guid fileId, CancellationToken cancellationToken)
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

            ShelfFileMetadata? metadata = await _fileStorageService.GetFileMetadataAsync(targetTenantId, fileId, cancellationToken);

            if (metadata == null)
                return NotFound();

            return Ok(metadata);
        }

        /// <summary>
        /// Creates metadata for a new file belonging to the authenticated tenant.
        /// </summary>
        /// <param name="metadata">The file metadata to create.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>The created file metadata.</returns>
        /// <response code="201">Returns the created file metadata.</response>
        /// <response code="400">If the metadata is invalid.</response>
        /// <response code="401">If the API key is invalid or missing.</response>
        /// <remarks>
        /// This endpoint creates the file metadata record for the authenticated tenant.
        /// The actual file content should be uploaded as chunks using the chunks endpoints
        /// before this metadata is created.
        /// </remarks>
        [HttpPost("metadata")]
        [ProducesResponseType(typeof(ShelfFileMetadata), 201)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        public async Task<ActionResult> CreateFileMetadata([FromBody] ShelfFileMetadata metadata, CancellationToken cancellationToken)
        {
            string tenantId = HttpContext.GetTenantId();
            await _fileStorageService.SaveFileMetadataAsync(tenantId, metadata, cancellationToken);
            return CreatedAtAction(nameof(GetFileMetadata), new { fileId = metadata.Id }, metadata);
        }

        /// <summary>
        /// Creates metadata for a new file belonging to a specific tenant.
        /// </summary>
        /// <param name="targetTenantId">The ID of the tenant for which to create the file metadata.</param>
        /// <param name="metadata">The file metadata to create.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>The created file metadata.</returns>
        /// <response code="201">Returns the created file metadata.</response>
        /// <response code="400">If the metadata is invalid.</response>
        /// <response code="401">If the API key is invalid or missing.</response>
        /// <response code="403">If the authenticated tenant does not have access to the specified tenant.</response>
        /// <response code="404">If the specified tenant does not exist.</response>
        /// <remarks>
        /// This endpoint allows a parent tenant to create file metadata for its subtenants.
        /// The authenticated tenant must have access to the specified tenant (either be the same tenant or a parent).
        /// The actual file content should be uploaded as chunks using the chunks endpoints
        /// before this metadata is created.
        /// </remarks>
        [HttpPost("{targetTenantId}/metadata")]
        [ProducesResponseType(typeof(ShelfFileMetadata), 201)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(404)]
        public async Task<ActionResult> CreateFileMetadataForTenant(string targetTenantId, [FromBody] ShelfFileMetadata metadata, CancellationToken cancellationToken)
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

            await _fileStorageService.SaveFileMetadataAsync(targetTenantId, metadata, cancellationToken);
            return CreatedAtAction(nameof(GetFileMetadataForTenant), new { targetTenantId, fileId = metadata.Id }, metadata);
        }

        /// <summary>
        /// Downloads a complete file by reconstructing it from all its chunks.
        /// </summary>
        /// <param name="fileId">The unique identifier of the file to download.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>The complete file as a binary stream.</returns>
        /// <response code="200">Returns the complete file as application/octet-stream.</response>
        /// <response code="404">If the file with the specified ID does not exist or does not belong to the tenant.</response>
        /// <response code="401">If the API key is invalid or missing.</response>
        /// <remarks>
        /// This endpoint reconstructs the complete file by reading all chunks in order
        /// and concatenating them into a single stream. The file is returned with the
        /// original filename and content type for proper browser handling.
        /// Only files belonging to the authenticated tenant can be downloaded.
        /// </remarks>
        [HttpGet("{fileId}/download")]
        [ProducesResponseType(typeof(FileStreamResult), 200)]
        [ProducesResponseType(404)]
        [ProducesResponseType(401)]
        public async Task<IActionResult> DownloadFile(Guid fileId, CancellationToken cancellationToken)
        {
            string tenantId = HttpContext.GetTenantId();

            // Get file metadata
            ShelfFileMetadata? metadata = await _fileStorageService.GetFileMetadataAsync(tenantId, fileId, cancellationToken);
            if (metadata == null)
                return NotFound();

            // Create a stream that reads all chunks in sequence
            Stream fileStream = await _fileStorageService.GetFileStreamAsync(tenantId, fileId, cancellationToken);

            // Return the file with proper headers for download
            return File(fileStream, metadata.ContentType, metadata.OriginalFilename);
        }

        /// <summary>
        /// Downloads a complete file by reconstructing it from all its chunks for a specific tenant.
        /// </summary>
        /// <param name="targetTenantId">The ID of the tenant whose file to download.</param>
        /// <param name="fileId">The unique identifier of the file to download.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>The complete file as a binary stream.</returns>
        /// <response code="200">Returns the complete file as application/octet-stream.</response>
        /// <response code="401">If the API key is invalid or missing.</response>
        /// <response code="403">If the authenticated tenant does not have access to the specified tenant.</response>
        /// <response code="404">If the file with the specified ID does not exist or the specified tenant does not exist.</response>
        /// <remarks>
        /// This endpoint allows a parent tenant to download files from its subtenants.
        /// The authenticated tenant must have access to the specified tenant (either be the same tenant or a parent).
        /// This endpoint reconstructs the complete file by reading all chunks in order
        /// and concatenating them into a single stream. The file is returned with the
        /// original filename and content type for proper browser handling.
        /// </remarks>
        [HttpGet("{targetTenantId}/{fileId}/download")]
        [ProducesResponseType(typeof(FileStreamResult), 200)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> DownloadFileForTenant(string targetTenantId, Guid fileId, CancellationToken cancellationToken)
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

            // Get file metadata
            ShelfFileMetadata? metadata = await _fileStorageService.GetFileMetadataAsync(targetTenantId, fileId, cancellationToken);
            if (metadata == null)
                return NotFound();

            // Create a stream that reads all chunks in sequence
            Stream fileStream = await _fileStorageService.GetFileStreamAsync(targetTenantId, fileId, cancellationToken);

            // Return the file with proper headers for download
            return File(fileStream, metadata.ContentType, metadata.OriginalFilename);
        }

        /// <summary>
        /// Deletes a file and all its associated chunks, scoped to the authenticated tenant.
        /// </summary>
        /// <param name="fileId">The unique identifier of the file to delete.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>No content on successful deletion.</returns>
        /// <response code="204">If the file was successfully deleted.</response>
        /// <response code="404">If the file with the specified ID does not exist or does not belong to the tenant.</response>
        /// <response code="401">If the API key is invalid or missing.</response>
        /// <remarks>
        /// This operation is idempotent - deleting a non-existent file will not throw an exception.
        /// The operation deletes both the file metadata and all associated chunks.
        /// Only files belonging to the authenticated tenant can be deleted.
        /// </remarks>
        [HttpDelete("{fileId}")]
        [ProducesResponseType(204)]
        [ProducesResponseType(404)]
        [ProducesResponseType(401)]
        public async Task<ActionResult> DeleteFile(Guid fileId, CancellationToken cancellationToken)
        {
            string tenantId = HttpContext.GetTenantId();
            bool? fileDeletedResult = await _fileStorageService.DeleteFileAsync(tenantId, fileId, cancellationToken);

            if (fileDeletedResult == null)
                return NotFound();

            return NoContent();
        }

        /// <summary>
        /// Deletes a file and all its associated chunks for a specific tenant.
        /// </summary>
        /// <param name="targetTenantId">The ID of the tenant whose file to delete.</param>
        /// <param name="fileId">The unique identifier of the file to delete.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>No content on successful deletion.</returns>
        /// <response code="204">If the file was successfully deleted.</response>
        /// <response code="401">If the API key is invalid or missing.</response>
        /// <response code="403">If the authenticated tenant does not have access to the specified tenant.</response>
        /// <response code="404">If the file with the specified ID does not exist or the specified tenant does not exist.</response>
        /// <remarks>
        /// This endpoint allows a parent tenant to delete files from its subtenants.
        /// The authenticated tenant must have access to the specified tenant (either be the same tenant or a parent).
        /// This operation is idempotent - deleting a non-existent file will not throw an exception.
        /// The operation deletes both the file metadata and all associated chunks.
        /// </remarks>
        [HttpDelete("{targetTenantId}/{fileId}")]
        [ProducesResponseType(204)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(404)]
        public async Task<ActionResult> DeleteFileForTenant(string targetTenantId, Guid fileId, CancellationToken cancellationToken)
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

            bool? fileDeletedResult = await _fileStorageService.DeleteFileAsync(targetTenantId, fileId, cancellationToken);

            if (fileDeletedResult == null)
                return NotFound();

            return NoContent();
        }
    }
}