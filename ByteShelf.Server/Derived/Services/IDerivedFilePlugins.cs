namespace ByteShelf.Server.Derived.Services
{
    /// <summary>The installed plugins that make derived files: everything derived files know of plugins comes from here.</summary>
    public interface IDerivedFilePlugins
    {
        /// <summary>Every one of them, the same while the server runs.</summary>
        IReadOnlyList<DerivedFilePlugin> All { get; }
    }
}
