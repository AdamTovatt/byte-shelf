using System.Threading;
using System.Threading.Tasks;
using ByteShelf.Configuration;
using Microsoft.AspNetCore.Mvc;

namespace ByteShelf.Controllers
{
    /// <summary>
    /// Controller for retrieving server configuration information.
    /// </summary>
    /// <remarks>
    /// This controller provides REST API endpoints for accessing server configuration
    /// that clients need to interact with the system properly.
    /// All endpoints require API key authentication unless authentication is disabled.
    /// </remarks>
    [ApiController]
    [Route("api/[controller]")]
    public class ConfigController : ControllerBase
    {
        private readonly ChunkConfiguration _chunkConfiguration;

        /// <summary>
        /// Initializes a new instance of the <see cref="ConfigController"/> class.
        /// </summary>
        /// <param name="chunkConfiguration">The chunk configuration settings.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="chunkConfiguration"/> is null.</exception>
        public ConfigController(ChunkConfiguration chunkConfiguration)
        {
            _chunkConfiguration = chunkConfiguration ?? throw new ArgumentNullException(nameof(chunkConfiguration));
        }

        /// <summary>
        /// Retrieves the chunk size configuration.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>The chunk configuration including the chunk size in bytes.</returns>
        /// <response code="200">Returns the chunk configuration.</response>
        /// <response code="401">If the API key is invalid or missing.</response>
        /// <remarks>
        /// This endpoint provides clients with the chunk size configuration they need
        /// to properly split files into chunks when uploading. The chunk size determines
        /// how large each piece of a file should be when it's split for storage.
        /// </remarks>
        [HttpGet("chunk-size")]
        [ProducesResponseType(typeof(ChunkConfiguration), 200)]
        [ProducesResponseType(401)]
        public ActionResult<ChunkConfiguration> GetChunkSize(CancellationToken cancellationToken)
        {
            return Ok(_chunkConfiguration);
        }
    }
} 