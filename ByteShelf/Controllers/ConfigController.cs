using System.Threading;
using System.Threading.Tasks;
using ByteShelf.Configuration;
using Microsoft.AspNetCore.Mvc;

namespace ByteShelf.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ConfigController : ControllerBase
    {
        private readonly ChunkConfiguration _chunkConfiguration;

        public ConfigController(ChunkConfiguration chunkConfiguration)
        {
            _chunkConfiguration = chunkConfiguration;
        }

        [HttpGet("chunk-size")]
        public ActionResult<ChunkConfiguration> GetChunkSize(CancellationToken cancellationToken)
        {
            return Ok(_chunkConfiguration);
        }
    }
} 