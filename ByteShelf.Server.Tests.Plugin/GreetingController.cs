using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ByteShelf.Server.Tests.Plugin
{
    /// <summary>Declares routes in every way a controller can, reaching for the server's own with some.</summary>
    [ApiController]
    [Route("/api/shelves")]
    public sealed class GreetingController : ControllerBase
    {
        private readonly PluginGreeting _greeting;

        public GreetingController(PluginGreeting greeting)
        {
            _greeting = greeting;
        }

        [HttpGet("relative")]
        public IActionResult Relative()
        {
            return Ok(_greeting);
        }

        [HttpGet("/api/version/current")]
        [HttpGet("~/absolute")]
        [ApiExplorerSettings(IgnoreApi = false)]
        public IActionResult Absolute()
        {
            return Ok("absolute");
        }

        [HttpGet]
        [Authorize]
        public IActionResult Guarded()
        {
            return Ok("guarded");
        }

        [HttpPost("write")]
        public IActionResult Write()
        {
            return Ok("written");
        }
    }
}
