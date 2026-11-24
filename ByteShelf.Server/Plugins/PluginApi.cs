namespace ByteShelf.Server.Plugins
{
    /// <summary>What this server offers plugins, as one number.</summary>
    public static class PluginApi
    {
        /// <summary>
        /// Raised on any breaking change to what plugins may use: the plugin folder, the assemblies they get
        /// from the server (<c>ByteShelf.Plugins</c>, <c>ByteShelfCommon</c> and the framework), and in the
        /// web UI the shared modules and <c>@byteshelf/plugin</c>. A plugin whose <c>plugin.json</c> names
        /// another number is not loaded. What is only added to <c>ByteShelf.Plugins</c> raises the minor of
        /// its assembly version, whose major is this; what is added to <c>ByteShelfCommon</c>, the minor of its own.
        /// </summary>
        public const int Version = 1;
    }
}
