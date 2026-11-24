namespace ByteShelf.Server.Derived.Services
{
    /// <summary>A server without plugins that make derived files.</summary>
    internal sealed class NoDerivedFilePlugins : IDerivedFilePlugins
    {
        public IReadOnlyList<DerivedFilePlugin> All => [];

        public IDerivedFileMaker? Load(string location)
        {
            return null;
        }
    }
}
