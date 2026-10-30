using System.Security.Cryptography;
using System.Text;
using ByteShelf.Server.Version.Models;

namespace ByteShelf.Server.Version.Services
{
    /// <summary>
    /// Identifies the running deploy: a hash of the server assembly's <c>ModuleVersionId</c> and the web
    /// UI's <c>index.html</c>. The build is deterministic, so the module id changes exactly when the
    /// server's code does; <c>index.html</c> names the content-hashed bundles, so it changes exactly when
    /// the web UI does. Either changing changes the id.
    /// </summary>
    public class VersionService : IVersionService
    {
        private readonly Lazy<string> _versionId;

        public VersionService(IWebHostEnvironment environment)
        {
            _versionId = new Lazy<string>(() => ComputeVersionId(environment.WebRootPath));
        }

        public VersionView GetCurrent()
        {
            return new VersionView { VersionId = _versionId.Value };
        }

        private static string ComputeVersionId(string? webRootPath)
        {
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData(Encoding.UTF8.GetBytes(typeof(VersionService).Assembly.ManifestModule.ModuleVersionId.ToString("N")));

            string? indexPath = string.IsNullOrEmpty(webRootPath) ? null : Path.Combine(webRootPath, "index.html");
            if (indexPath != null && File.Exists(indexPath))
            {
                hash.AppendData(File.ReadAllBytes(indexPath));
            }

            return Convert.ToHexStringLower(hash.GetHashAndReset())[..32];
        }
    }
}
