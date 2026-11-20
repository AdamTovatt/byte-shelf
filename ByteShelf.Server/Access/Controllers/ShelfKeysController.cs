using ByteShelf.Server.Access.Models;
using ByteShelf.Server.Access.Services;
using ByteShelfCommon;
using Microsoft.AspNetCore.Mvc;

namespace ByteShelf.Server.Access.Controllers
{
    /// <summary>The keys programs use the shelf with. Admin only.</summary>
    [ApiController]
    [Route("api/shelves/{shelf}/keys")]
    public class ShelfKeysController : ShelfControllerBase
    {
        private readonly IShelfKeyService _keyService;

        public ShelfKeysController(IShelfKeyService keyService)
        {
            _keyService = keyService;
        }

        /// <summary>The keys with a grant on the shelf, each with all its shelves, without their secrets.</summary>
        [HttpGet]
        [ShelfAccess(ShelfOperation.Administer)]
        [ProducesResponseType(typeof(IReadOnlyList<ShelfKeyInfo>), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 403)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> List()
        {
            return Ok(await _keyService.GetForShelfAsync(Shelf));
        }

        /// <summary>Creates a key and answers its secret, the one time it is shown.</summary>
        [HttpPost]
        [ShelfAccess(ShelfOperation.Administer)]
        [ProducesResponseType(typeof(CreatedShelfKey), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 403)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> Create([FromBody] CreateShelfKeyRequest request)
        {
            return Ok(await _keyService.CreateAsync(Shelf, request.Name, request.Permission));
        }

        /// <summary>Takes the shelf from a key; the key is deleted when that was its last shelf.</summary>
        [HttpDelete("{keyId:guid}")]
        [ShelfAccess(ShelfOperation.Administer)]
        [ProducesResponseType(204)]
        [ProducesResponseType(typeof(ErrorResponse), 403)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> Delete(Guid keyId)
        {
            await _keyService.RemoveGrantAsync(Shelf, keyId);
            return NoContent();
        }
    }
}
