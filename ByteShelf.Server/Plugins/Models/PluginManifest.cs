using ByteShelfCommon;

namespace ByteShelf.Server.Plugins.Models
{
    /// <summary>A plugin's <c>plugin.json</c>, valid and for this server's <see cref="PluginApi.Version"/>.</summary>
    /// <param name="Declaration">What the plugin is called everywhere (the folder's name means nothing), what a person sees it called, and the fields it declares in its documents.</param>
    /// <param name="Version">The plugin's version, for a person to read.</param>
    /// <param name="ServerAssemblyPath">The full path of its server assembly; null when it has none.</param>
    public sealed record PluginManifest(PluginDeclaration Declaration, string Version, string? ServerAssemblyPath)
    {
        public string Id => Declaration.Id;

        public string Name => Declaration.Name;
    }
}
