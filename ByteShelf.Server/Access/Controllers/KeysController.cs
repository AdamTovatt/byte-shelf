using ByteShelf.Server.Access.Models;
using ByteShelf.Server.Access.Services;
using ByteShelf.Server.Shared.Auth;
using ByteShelfCommon;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ByteShelf.Server.Access.Controllers
{
    /// <summary>
    /// A key by its id, across every shelf it works on. Admin only: the actions naming a shelf ask
    /// <see cref="ShelfAccessAttribute"/> for <see cref="ShelfOperation.Administer"/> there, the others
    /// the admin role.
    /// </summary>
    [ApiController]
    [Route("api/keys/{keyId:guid}")]
    public class KeysController : ControllerBase
    {
        private readonly IShelfKeyService _keyService;

        public KeysController(IShelfKeyService keyService)
        {
            _keyService = keyService;
        }

        /// <summary>The shelves the key works on, and what it may do on each.</summary>
        [HttpGet("grants")]
        [Authorize(Roles = UserRoles.Admin)]
        [ProducesResponseType(typeof(IReadOnlyList<ShelfKeyGrantInfo>), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> ListGrants(Guid keyId)
        {
            return Ok(await _keyService.GetGrantsAsync(keyId));
        }

        /// <summary>Gives the key a shelf, or changes what it may do there.</summary>
        [HttpPut("grants/{shelf}")]
        [ShelfAccess(ShelfOperation.Administer)]
        [ProducesResponseType(typeof(ShelfKeyGrantInfo), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 403)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> SetGrant(Guid keyId, [FromBody] SetShelfKeyGrantRequest request)
        {
            return Ok(await _keyService.SetGrantAsync(HttpContext.GetShelfAccess().Shelf, keyId, request.Permission));
        }

        /// <summary>Takes a shelf from the key; the key is deleted when that was its last shelf.</summary>
        [HttpDelete("grants/{shelf}")]
        [ShelfAccess(ShelfOperation.Administer)]
        [ProducesResponseType(204)]
        [ProducesResponseType(typeof(ErrorResponse), 403)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> RemoveGrant(Guid keyId)
        {
            await _keyService.RemoveGrantAsync(HttpContext.GetShelfAccess().Shelf, keyId);
            return NoContent();
        }

        /// <summary>Deletes the key from every shelf; requests with it fail from then on.</summary>
        [HttpDelete]
        [Authorize(Roles = UserRoles.Admin)]
        [ProducesResponseType(204)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> Delete(Guid keyId)
        {
            await _keyService.DeleteAsync(keyId);
            return NoContent();
        }
    }
}
