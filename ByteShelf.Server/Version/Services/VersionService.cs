using ByteShelf.Server.Plugins.Models;
using ByteShelf.Server.Plugins.Services;
using ByteShelf.Server.Shared;
using ByteShelf.Server.Shared.StaticFiles;
using ByteShelf.Server.Version.Models;
using ByteShelfCommon;

namespace ByteShelf.Server.Version.Services
{
    /// <summary>
    /// Names the release this server runs, and identifies the running deploy: a hash of the server
    /// assembly's <c>ModuleVersionId</c>, the web UI's <c>index.html</c> and the web UI parts of the
    /// plugins. The build is deterministic, so the module id changes exactly when the server's code does;
    /// <c>index.html</c> names the content-hashed bundles, so it changes exactly when the web UI does, and
    /// each plugin's hash is of its web UI files. Any of them changing changes the id.
    /// </summary>
    public class VersionService : IVersionService
    {
        /// <summary>The release this server was built as: the csproj's <c>Version</c>, which a release's tag must name.</summary>
        private static readonly string Release = ReleaseVersion.Of(typeof(VersionService).Assembly);

        private readonly Lazy<string> _versionId;

        public VersionService(IWebHostEnvironment environment, IPluginCatalog plugins)
        {
            _versionId = new Lazy<string>(() => ComputeVersionId(environment.WebRootPath, plugins));
        }

        public VersionView GetCurrent()
        {
            return new VersionView { VersionId = _versionId.Value };
        }

        public ServerVersion GetRelease()
        {
            return new ServerVersion { Version = Release, VersionId = _versionId.Value };
        }

        private static string ComputeVersionId(string? webRootPath, IPluginCatalog plugins)
        {
            using ShortHash hash = new ShortHash();
            hash.Add(typeof(VersionService).Assembly.ManifestModule.ModuleVersionId.ToString("N"));

            string? indexPath = StaticFileCachePolicy.FindIndexHtml(webRootPath);
            if (indexPath != null)
            {
                hash.AddFile(indexPath);
            }

            foreach (PluginUi ui in plugins.Loaded.Select(plugin => plugin.Ui).OfType<PluginUi>())
            {
                hash.Add(ui.PluginId);
                hash.Add(ui.Hash);
            }

            return hash.Finish();
        }
    }
}
