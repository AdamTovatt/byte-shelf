namespace ByteShelf.Server.Metadata.Services
{
    /// <summary>A server without plugins.</summary>
    internal sealed class NoMetadataPlugins : IMetadataPlugins
    {
        public IReadOnlyList<MetadataPlugin> All => [];
    }
}
