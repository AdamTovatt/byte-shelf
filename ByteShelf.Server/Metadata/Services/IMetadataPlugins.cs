namespace ByteShelf.Server.Metadata.Services
{
    /// <summary>The installed plugins, as far as metadata goes: everything metadata knows of plugins comes from here.</summary>
    public interface IMetadataPlugins
    {
        /// <summary>Every installed plugin, which stays the same while the server runs.</summary>
        IReadOnlyList<MetadataPlugin> All { get; }
    }
}
