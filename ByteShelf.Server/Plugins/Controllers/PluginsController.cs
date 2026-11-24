using ByteShelf.Server.Access.Auth;
using ByteShelf.Server.Metadata.Services;
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
        private readonly ReaderProblems _readerProblems;

        public PluginsController(IPluginCatalog catalog, ReaderProblems readerProblems)
        {
            _catalog = catalog;
            _readerProblems = readerProblems;
        }

        /// <summary>
        /// The plugins this server loaded, for any signed-in user or shelf key, and for an admin those it
        /// skipped and why, and why one's reader could not run. Not public, as the release is not: the version
        /// of a plugin released with the server is the server's.
        /// </summary>
        [HttpGet]
        [Authorize(Policy = ShelfKeyAuthentication.ShelfCallerPolicy)]
        [ProducesResponseType(typeof(PluginList), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        public IActionResult List()
        {
            bool isAdmin = User.IsInRole(UserRoles.Admin);
            return Ok(new PluginList
            {
                Plugins = _catalog.Loaded.Select(plugin => ViewOf(plugin, isAdmin)).ToList(),
                Skipped = isAdmin ? _catalog.Skipped : null,
            });
        }

        private PluginView ViewOf(LoadedPlugin plugin, bool isAdmin)
        {
            return new PluginView
            {
                Id = plugin.Manifest.Id,
                Name = plugin.Manifest.Name,
                Version = plugin.Manifest.Version,
                ModuleUrl = plugin.Ui?.ModuleAddress,
                StylesheetUrl = plugin.Ui?.StylesheetAddress,
                ReaderProblem = isAdmin ? _readerProblems.Of(plugin.Manifest.Id) : null,
            };
        }
    }
}
