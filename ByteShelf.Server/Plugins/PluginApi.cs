namespace ByteShelf.Server.Plugins
{
    /// <summary>What this server offers plugins, as one number.</summary>
    public static class PluginApi
    {
        /// <summary>
        /// The <c>api</c> a plugin's <c>plugin.json</c> must name to be loaded, and the major of
        /// <c>ByteShelf.Plugins</c>' assembly version. What it covers, and when it is raised, is in Plugins.md,
        /// Versions.
        /// </summary>
        public const int Version = 1;
    }
}
