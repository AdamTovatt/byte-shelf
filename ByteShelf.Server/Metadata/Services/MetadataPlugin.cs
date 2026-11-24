namespace ByteShelf.Server.Metadata.Services
{
    /// <summary>An installed plugin, as far as metadata goes.</summary>
    /// <param name="Id">The plugin's id, which its documents carry: lowercase letters, digits and hyphens, and no other installed plugin's, both checked by what loads plugins.</param>
    /// <param name="Reader">What reads files' contents for it; null for a plugin that reads none.</param>
    public sealed record MetadataPlugin(string Id, MetadataPluginReader? Reader = null);
}
