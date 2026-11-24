using ByteShelf.Server.Plugins.Models;
using ByteShelf.Server.Shared;

namespace ByteShelf.Server.Plugins.Services
{
    /// <summary>Reads a plugin's <c>ui</c> folder.</summary>
    public static class PluginUiReader
    {
        private const string FolderName = "ui";

        /// <summary>The web UI part of the plugin <paramref name="pluginId"/> in <paramref name="pluginFolder"/>; null when it has none.</summary>
        public static PluginUi? Read(string pluginId, string pluginFolder)
        {
            string folder = Path.Combine(pluginFolder, FolderName);
            return File.Exists(Path.Combine(folder, PluginUi.ModuleFileName))
                ? new PluginUi(pluginId, folder, HashOf(folder), File.Exists(Path.Combine(folder, PluginUi.StylesheetFileName)))
                : null;
        }

        private static string HashOf(string folder)
        {
            using ShortHash hash = new ShortHash();
            foreach (string file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories).OrderBy(file => file, StringComparer.Ordinal))
            {
                hash.Add(Path.GetRelativePath(folder, file));
                hash.AddFile(file);
            }

            return hash.Finish();
        }
    }
}
