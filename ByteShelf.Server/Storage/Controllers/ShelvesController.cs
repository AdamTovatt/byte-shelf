using ByteShelf.Server.Access;
using ByteShelf.Server.Access.Auth;
using ByteShelf.Server.Access.Controllers;
using ByteShelf.Server.Access.Models;
using ByteShelf.Server.Access.Services;
using ByteShelf.Server.Shared.Auth;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Services;
using ByteShelfCommon;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ByteShelf.Server.Storage.Controllers
{
    /// <summary>
    /// The shelves themselves. Anyone with access lists and reads the shelves they can use; only admins
    /// create, change and delete them.
    /// </summary>
    [ApiController]
    [Route("api/shelves")]
    public class ShelvesController : ShelfControllerBase
    {
        private readonly IShelfService _shelfService;
        private readonly IShelfAccessResolver _accessResolver;

        public ShelvesController(IShelfService shelfService, IShelfAccessResolver accessResolver)
        {
            _shelfService = shelfService;
            _accessResolver = accessResolver;
        }

        /// <summary>Every shelf the caller can use, with its usage, by name: all of them for an admin, its own for a key.</summary>
        [HttpGet]
        [Authorize(Policy = ShelfKeyAuthentication.ShelfCallerPolicy)]
        [ProducesResponseType(typeof(IReadOnlyList<ShelfInfo>), 200)]
        public async Task<IActionResult> List()
        {
            IReadOnlyList<Shelf> shelves = await _accessResolver.GetVisibleShelvesAsync(HttpContext.GetShelfCaller());
            return Ok(await _shelfService.GetInfosAsync(shelves));
        }

        /// <summary>One shelf with its usage.</summary>
        [HttpGet("{shelf}")]
        [ShelfAccess(ShelfOperation.Read)]
        [ProducesResponseType(typeof(ShelfInfo), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> Get()
        {
            return Ok(await _shelfService.GetInfoAsync(Shelf));
        }

        /// <summary>Creates a shelf. Admin only.</summary>
        [HttpPost]
        [Authorize(Roles = UserRoles.Admin)]
        [ProducesResponseType(typeof(ShelfInfo), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 409)]
        public async Task<IActionResult> Create([FromBody] CreateShelfRequest request)
        {
            Shelf shelf = await _shelfService.CreateAsync(request.Name, request.QuotaBytes);
            return Ok(await _shelfService.GetInfoAsync(shelf));
        }

        /// <summary>Replaces a shelf's name and quota. Admin only. A new name changes the shelf's URLs.</summary>
        [HttpPut("{shelf}")]
        [ShelfAccess(ShelfOperation.Administer)]
        [ProducesResponseType(typeof(ShelfInfo), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 403)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        [ProducesResponseType(typeof(ErrorResponse), 409)]
        public async Task<IActionResult> Update([FromBody] UpdateShelfRequest request)
        {
            Shelf updated = await _shelfService.UpdateAsync(Shelf, request.Name, request.QuotaBytes);
            return Ok(await _shelfService.GetInfoAsync(updated));
        }

        /// <summary>
        /// Deletes a shelf with its access grants and keys. Admin only; refused while it holds anything,
        /// the trash included.
        /// </summary>
        [HttpDelete("{shelf}")]
        [ShelfAccess(ShelfOperation.Administer)]
        [ProducesResponseType(204)]
        [ProducesResponseType(typeof(ErrorResponse), 403)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        [ProducesResponseType(typeof(ErrorResponse), 409)]
        public async Task<IActionResult> Delete()
        {
            await _shelfService.DeleteAsync(Shelf);
            return NoContent();
        }
    }
}
