namespace ByteShelf.Server.Plugins.Models
{
    /// <summary>A plugin's <c>plugin.json</c>, valid and for this server's <see cref="PluginApi.Version"/>.</summary>
    /// <param name="Id">Lowercase letters, digits and hyphens. What the plugin is called everywhere; the folder's name means nothing.</param>
    /// <param name="Name">What a person sees it called.</param>
    /// <param name="Version">The plugin's version, for a person to read.</param>
    /// <param name="ServerAssemblyPath">The full path of its server assembly; null when it has none.</param>
    public sealed record PluginManifest(string Id, string Name, string Version, string? ServerAssemblyPath);
}
