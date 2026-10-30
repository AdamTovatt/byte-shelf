using Microsoft.AspNetCore.Mvc;
using ByteShelf.Server.Version.Models;
using ByteShelf.Server.Version.Services;

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
        public ActionResult<VersionView> GetCurrent()
        {
            return Ok(_versionService.GetCurrent());
        }
    }
}
