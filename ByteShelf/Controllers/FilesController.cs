using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ByteShelf.Services;
using ByteShelfCommon;
using Microsoft.AspNetCore.Mvc;

namespace ByteShelf.Controllers
{
    /// <summary>
    /// Controller for managing file metadata and operations.
    /// </summary>
    /// <remarks>
    /// This controller provides REST API endpoints for file operations including:
    /// - Listing all files
    /// - Retrieving file metadata
    /// - Creating file metadata
    /// - Deleting files and their associated chunks
    /// All endpoints require API key authentication unless authentication is disabled.
    /// </remarks>
    [ApiController]
    [Route("api/[controller]")]
    public class FilesController : ControllerBase
    {
        private readonly IFileStorageService _fileStorageService;

        /// <summary>
        /// Initializes a new instance of the <see cref="FilesController"/> class.
        /// </summary>
        /// <param name="fileStorageService">The file storage service for file operations.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="fileStorageService"/> is null.</exception>
        public FilesController(IFileStorageService fileStorageService)
        {
            _fileStorageService = fileStorageService ?? throw new ArgumentNullException(nameof(fileStorageService));
        }

        /// <summary>
        /// Retrieves metadata for all stored files.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A collection of file metadata for all stored files.</returns>
        /// <response code="200">Returns the list of file metadata.</response>
        /// <response code="401">If the API key is invalid or missing.</response>
        /// <remarks>
        /// This endpoint returns metadata only, not the actual file content.
        /// Use the chunks endpoints to retrieve file content.
        /// </remarks>
        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<ShelfFileMetadata>), 200)]
        [ProducesResponseType(401)]
        public async Task<ActionResult<IEnumerable<ShelfFileMetadata>>> GetFiles(CancellationToken cancellationToken)
        {
            IEnumerable<ShelfFileMetadata> files = await _fileStorageService.GetFilesAsync(cancellationToken);
            return Ok(files);
        }

        /// <summary>
        /// Retrieves metadata for a specific file by its ID.
        /// </summary>
        /// <param name="fileId">The unique identifier of the file.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>The file metadata if found.</returns>
        /// <response code="200">Returns the file metadata.</response>
        /// <response code="404">If the file with the specified ID does not exist.</response>
        /// <response code="401">If the API key is invalid or missing.</response>
        /// <remarks>
        /// This endpoint returns metadata only, not the actual file content.
        /// Use the chunks endpoints to retrieve file content.
        /// </remarks>
        [HttpGet("{fileId}/metadata")]
        [ProducesResponseType(typeof(ShelfFileMetadata), 200)]
        [ProducesResponseType(404)]
        [ProducesResponseType(401)]
        public async Task<ActionResult<ShelfFileMetadata>> GetFileMetadata(Guid fileId, CancellationToken cancellationToken)
        {
            ShelfFileMetadata? metadata = await _fileStorageService.GetFileMetadataAsync(fileId, cancellationToken);
            
            if (metadata == null)
                return NotFound();

            return Ok(metadata);
        }

        /// <summary>
        /// Creates metadata for a new file.
        /// </summary>
        /// <param name="metadata">The file metadata to create.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>The created file metadata.</returns>
        /// <response code="201">Returns the created file metadata.</response>
        /// <response code="400">If the metadata is invalid.</response>
        /// <response code="401">If the API key is invalid or missing.</response>
        /// <remarks>
        /// This endpoint creates the file metadata record. The actual file content should be uploaded
        /// as chunks using the chunks endpoints before this metadata is created.
        /// </remarks>
        [HttpPost("metadata")]
        [ProducesResponseType(typeof(ShelfFileMetadata), 201)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        public async Task<ActionResult> CreateFileMetadata([FromBody] ShelfFileMetadata metadata, CancellationToken cancellationToken)
        {
            await _fileStorageService.SaveFileMetadataAsync(metadata, cancellationToken);
            return CreatedAtAction(nameof(GetFileMetadata), new { fileId = metadata.Id }, metadata);
        }

        /// <summary>
        /// Deletes a file and all its associated chunks.
        /// </summary>
        /// <param name="fileId">The unique identifier of the file to delete.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>No content on successful deletion.</returns>
        /// <response code="204">If the file was successfully deleted.</response>
        /// <response code="401">If the API key is invalid or missing.</response>
        /// <remarks>
        /// This operation is idempotent - deleting a non-existent file will not throw an exception.
        /// The operation deletes both the file metadata and all associated chunks.
        /// </remarks>
        [HttpDelete("{fileId}")]
        [ProducesResponseType(204)]
        [ProducesResponseType(401)]
        public async Task<ActionResult> DeleteFile(Guid fileId, CancellationToken cancellationToken)
        {
            await _fileStorageService.DeleteFileAsync(fileId, cancellationToken);
            return NoContent();
        }
    }
} 