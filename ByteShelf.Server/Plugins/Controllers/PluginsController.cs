using ByteShelf.Server.Access.Auth;
using ByteShelf.Server.Plugins.Models;
using ByteShelf.Server.Plugins.Services;
using ByteShelfCommon;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ByteShelf.Server.Plugins.Controllers
{
    [ApiController]
    [Route("api/plugins")]
    public class PluginsController : ControllerBase
    {
        private readonly IPluginCatalog _catalog;

        public PluginsController(IPluginCatalog catalog)
        {
            _catalog = catalog;
        }

        /// <summary>
        /// The plugins this server loaded, for any signed-in user or shelf key, and for an admin those it
        /// skipped and why. Not public, as the release is not: the version of a plugin released with the
        /// server is the server's.
        /// </summary>
        [HttpGet]
        [Authorize(Policy = ShelfKeyAuthentication.ShelfCallerPolicy)]
        [ProducesResponseType(typeof(PluginList), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        public IActionResult List()
        {
            return Ok(new PluginList
            {
                Plugins = _catalog.Loaded.Select(ViewOf).ToList(),
                Skipped = User.IsInRole(UserRoles.Admin) ? _catalog.Skipped : null,
            });
        }

        private static PluginView ViewOf(LoadedPlugin plugin)
        {
            return new PluginView
            {
                Id = plugin.Manifest.Id,
                Name = plugin.Manifest.Name,
                Version = plugin.Manifest.Version,
                ModuleUrl = plugin.Ui?.ModuleAddress,
                StylesheetUrl = plugin.Ui?.StylesheetAddress,
            };
        }
    }
}
