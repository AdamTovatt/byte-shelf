using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ByteShelf.Services;
using Microsoft.AspNetCore.Mvc;

namespace ByteShelf.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ChunksController : ControllerBase
    {
        private readonly IFileStorageService _fileStorageService;

        public ChunksController(IFileStorageService fileStorageService)
        {
            _fileStorageService = fileStorageService;
        }

        [HttpGet("{chunkId}")]
        public async Task<IActionResult> GetChunk(Guid chunkId, CancellationToken cancellationToken)
        {
            try
            {
                Stream chunkStream = await _fileStorageService.GetChunkAsync(chunkId, cancellationToken);
                return File(chunkStream, "application/octet-stream");
            }
            catch (FileNotFoundException)
            {
                return NotFound();
            }
        }

        [HttpPut("{chunkId}")]
        public async Task<IActionResult> UploadChunk(Guid chunkId, CancellationToken cancellationToken)
        {
            if (Request.Body == null)
                return BadRequest("No content provided");

            Guid savedChunkId = await _fileStorageService.SaveChunkAsync(chunkId, Request.Body, cancellationToken);
            return Ok(new { ChunkId = savedChunkId });
        }
    }
} 