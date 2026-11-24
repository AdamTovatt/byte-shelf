namespace ByteShelf.Server.Plugins.Models
{
    /// <summary>
    /// A plugin's part in the web UI: its <c>ui</c> folder, which is all of it that is served, at an
    /// address holding a hash of everything in it, so a browser may keep each file for good.
    /// </summary>
    /// <param name="PluginId">The plugin it is part of.</param>
    /// <param name="Folder">The <c>ui</c> folder's full path.</param>
    /// <param name="Hash">Of the names and contents of every file in it.</param>
    /// <param name="HasStylesheet">Whether it has a <see cref="StylesheetFileName"/> beside its <see cref="ModuleFileName"/>.</param>
    public sealed record PluginUi(string PluginId, string Folder, string Hash, bool HasStylesheet)
    {
        public const string ModuleFileName = "plugin.js";

        public const string StylesheetFileName = "plugin.css";

        /// <summary>Where the folder is served.</summary>
        public string RequestPath => $"/plugins/{PluginId}/{Hash}";

        public string ModuleAddress => $"{RequestPath}/{ModuleFileName}";

        /// <summary>Null when it has no stylesheet.</summary>
        public string? StylesheetAddress => HasStylesheet ? $"{RequestPath}/{StylesheetFileName}" : null;
    }
}
