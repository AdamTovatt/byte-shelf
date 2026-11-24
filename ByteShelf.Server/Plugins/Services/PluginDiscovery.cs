using ByteShelf.Server.Plugins.Models;

namespace ByteShelf.Server.Plugins.Services
{
    /// <summary>Finds the folders that should hold plugins. It loads nothing.</summary>
    public static class PluginDiscovery
    {
        /// <summary>
        /// Every folder directly under <paramref name="pluginsPath"/>, and every link, so one that leads to
        /// no folder is told of rather than left out; a folder that several lead to is found once. None when
        /// it is not set, or cannot be read, which is logged.
        /// </summary>
        public static IReadOnlyList<PluginCandidate> Find(string? pluginsPath, ILogger logger)
        {
            if (string.IsNullOrWhiteSpace(pluginsPath))
            {
                return [];
            }

            try
            {
                return Directory.GetFileSystemEntries(pluginsPath)
                    .Where(entry => Directory.Exists(entry) || new FileInfo(entry).LinkTarget != null)
                    .Select(PluginManifestReader.Read)
                    .DistinctBy(candidate => candidate.Folder)
                    .ToList();
            }
            catch (Exception exception)
            {
                logger.LogError("No plugins are loaded: {Variable}, {Path}, could not be read: {Message}", EnvironmentVariables.Plugins.Name, pluginsPath, exception.Message);
                return [];
            }
        }
    }
}
