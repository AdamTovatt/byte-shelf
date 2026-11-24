using ByteShelfCommon;

namespace ByteShelf.Server.Metadata.Services
{
    /// <summary>An installed plugin, as far as metadata goes.</summary>
    /// <param name="Declaration">What it declares. Its id is no other installed plugin's, checked by what loads plugins.</param>
    /// <param name="Reader">What reads files' contents for it; null for a plugin that reads none.</param>
    public sealed record MetadataPlugin(PluginDeclaration Declaration, MetadataPluginReader? Reader = null);
}
