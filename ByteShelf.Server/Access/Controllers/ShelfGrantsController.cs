using ByteShelf.Server.Access.Models;
using ByteShelf.Server.Access.Services;
using ByteShelfCommon;
using Microsoft.AspNetCore.Mvc;

namespace ByteShelf.Server.Access.Controllers
{
    /// <summary>Which users may use a shelf, and how. Admin only.</summary>
    [ApiController]
    [Route("api/shelves/{shelf}/access")]
    public class ShelfGrantsController : ShelfControllerBase
    {
        private readonly IShelfGrantService _grantService;

        public ShelfGrantsController(IShelfGrantService grantService)
        {
            _grantService = grantService;
        }

        /// <summary>Every user given access to the shelf.</summary>
        [HttpGet]
        [ShelfAccess(ShelfOperation.Administer)]
        [ProducesResponseType(typeof(IReadOnlyList<ShelfAccessGrant>), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 403)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> List()
        {
            return Ok(await _grantService.GetForShelfAsync(Shelf));
        }

        /// <summary>Gives a user access to the shelf, or replaces the access they have.</summary>
        [HttpPut("{userId:guid}")]
        [ShelfAccess(ShelfOperation.Administer)]
        [ProducesResponseType(typeof(ShelfAccessGrant), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 403)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> Set(Guid userId, [FromBody] SetShelfAccessRequest request)
        {
            return Ok(await _grantService.SetAsync(Shelf, userId, request.Permission));
        }

        /// <summary>Takes a user's access to the shelf away.</summary>
        [HttpDelete("{userId:guid}")]
        [ShelfAccess(ShelfOperation.Administer)]
        [ProducesResponseType(204)]
        [ProducesResponseType(typeof(ErrorResponse), 403)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> Remove(Guid userId)
        {
            await _grantService.RemoveAsync(Shelf, userId);
            return NoContent();
        }
    }
}
