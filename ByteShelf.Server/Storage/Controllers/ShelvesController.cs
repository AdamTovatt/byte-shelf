using ByteShelf.Server.Shared.Auth;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Services;
using ByteShelfCommon;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ByteShelf.Server.Storage.Controllers
{
    /// <summary>The shelves themselves. Admin only.</summary>
    [ApiController]
    [Route("api/shelves")]
    [Authorize(Roles = UserRole.Admin)]
    public class ShelvesController : ControllerBase
    {
        private readonly IShelfService _shelfService;

        public ShelvesController(IShelfService shelfService)
        {
            _shelfService = shelfService;
        }

        /// <summary>Every shelf with its usage, by name.</summary>
        [HttpGet]
        [ProducesResponseType(typeof(IReadOnlyList<ShelfInfo>), 200)]
        public async Task<IActionResult> List()
        {
            return Ok(await _shelfService.GetAllAsync());
        }

        /// <summary>One shelf with its usage.</summary>
        [HttpGet("{shelf}")]
        [ProducesResponseType(typeof(ShelfInfo), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> Get(string shelf)
        {
            return Ok(await _shelfService.GetInfoAsync(await _shelfService.GetByNameAsync(shelf)));
        }

        /// <summary>Creates a shelf.</summary>
        [HttpPost]
        [ProducesResponseType(typeof(ShelfInfo), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 409)]
        public async Task<IActionResult> Create([FromBody] CreateShelfRequest request)
        {
            Shelf shelf = await _shelfService.CreateAsync(request.Name, request.QuotaBytes);
            return Ok(await _shelfService.GetInfoAsync(shelf));
        }
    }
}
