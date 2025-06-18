using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ByteShelf.Services;
using ByteShelfCommon;
using Microsoft.AspNetCore.Mvc;

namespace ByteShelf.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FilesController : ControllerBase
    {
        private readonly IFileStorageService _fileStorageService;

        public FilesController(IFileStorageService fileStorageService)
        {
            _fileStorageService = fileStorageService;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<ShelfFileMetadata>>> GetFiles(CancellationToken cancellationToken)
        {
            IEnumerable<ShelfFileMetadata> files = await _fileStorageService.GetFilesAsync(cancellationToken);
            return Ok(files);
        }

        [HttpGet("{fileId}/metadata")]
        public async Task<ActionResult<ShelfFileMetadata>> GetFileMetadata(Guid fileId, CancellationToken cancellationToken)
        {
            ShelfFileMetadata? metadata = await _fileStorageService.GetFileMetadataAsync(fileId, cancellationToken);
            
            if (metadata == null)
                return NotFound();

            return Ok(metadata);
        }

        [HttpPost("metadata")]
        public async Task<ActionResult> CreateFileMetadata([FromBody] ShelfFileMetadata metadata, CancellationToken cancellationToken)
        {
            await _fileStorageService.SaveFileMetadataAsync(metadata, cancellationToken);
            return CreatedAtAction(nameof(GetFileMetadata), new { fileId = metadata.Id }, metadata);
        }

        [HttpDelete("{fileId}")]
        public async Task<ActionResult> DeleteFile(Guid fileId, CancellationToken cancellationToken)
        {
            await _fileStorageService.DeleteFileAsync(fileId, cancellationToken);
            return NoContent();
        }
    }
} 