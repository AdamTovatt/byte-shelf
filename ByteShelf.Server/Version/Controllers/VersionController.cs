using ByteShelf.Server.Access.Auth;
using ByteShelf.Server.Version.Models;
using ByteShelf.Server.Version.Services;
using ByteShelfCommon;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ByteShelf.Server.Version.Controllers
{
    [ApiController]
    [Route("api/version")]
    public class VersionController : ControllerBase
    {
        private readonly IVersionService _versionService;

        public VersionController(IVersionService versionService)
        {
            _versionService = versionService;
        }

        /// <summary>
        /// Returns the running build's version id. Intentionally unauthenticated: the frontend
        /// polls this on the login screen too, and the value is not sensitive.
        /// </summary>
        [HttpGet("current")]
        [ProducesResponseType(typeof(VersionView), 200)]
        public IActionResult GetCurrent()
        {
            return Ok(_versionService.GetCurrent());
        }

        /// <summary>
        /// The release this server runs, with the deploy id, for any signed-in user or shelf key. Not
        /// public: an anonymous caller who knew the release could match the server against its known bugs.
        /// </summary>
        [HttpGet("release")]
        [Authorize(Policy = ShelfKeyAuthentication.ShelfCallerPolicy)]
        [ProducesResponseType(typeof(ServerVersion), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        public IActionResult GetRelease()
        {
            return Ok(_versionService.GetRelease());
        }
    }
}
