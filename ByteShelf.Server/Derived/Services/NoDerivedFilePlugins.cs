namespace ByteShelf.Server.Derived.Services
{
    /// <summary>None, as where plugins are not loaded.</summary>
    internal sealed class NoDerivedFilePlugins : IDerivedFilePlugins
    {
        public IReadOnlyList<DerivedFilePlugin> All => [];
    }
}
