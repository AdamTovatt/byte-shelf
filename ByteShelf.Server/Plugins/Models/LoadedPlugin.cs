namespace ByteShelf.Server.Plugins.Models
{
    /// <summary>A plugin this server loaded.</summary>
    /// <param name="Manifest">Its <c>plugin.json</c>.</param>
    /// <param name="Ui">Its part in the web UI; null when it has none.</param>
    /// <param name="Reader">What it reads files' contents with; null when it reads none.</param>
    public sealed record LoadedPlugin(PluginManifest Manifest, PluginUi? Ui, PluginReaderRegistration? Reader);
}
