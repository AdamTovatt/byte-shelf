using Microsoft.AspNetCore.Mvc;

namespace ByteShelf.Server.Tests.Plugin
{
    /// <summary>Declares no route of its own; its action does.</summary>
    public sealed class UnroutedController : Controller
    {
        [HttpGet("bare")]
        public IActionResult Bare()
        {
            return Ok("bare");
        }
    }
}
