namespace ByteShelf.Server.Plugins.Models
{
    /// <summary>The plugins this server loaded and, for an admin, those it did not.</summary>
    public sealed class PluginList
    {
        public required IReadOnlyList<PluginView> Plugins { get; init; }

        /// <summary>Null for anyone but an admin: the reasons name the server's folders.</summary>
        public required IReadOnlyList<SkippedPlugin>? Skipped { get; init; }
    }
}
