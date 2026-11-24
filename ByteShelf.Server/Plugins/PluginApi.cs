namespace ByteShelf.Server.Plugins
{
    /// <summary>What this server offers plugins, as one number.</summary>
    public static class PluginApi
    {
        /// <summary>
        /// Raised on any breaking change to what plugins may use: the plugin folder, <c>ByteShelf.Plugins</c>,
        /// and in the web UI the shared modules and <c>@byteshelf/plugin</c>. A plugin whose
        /// <c>plugin.json</c> names another number is not loaded.
        /// </summary>
        public const int Version = 1;
    }
}
