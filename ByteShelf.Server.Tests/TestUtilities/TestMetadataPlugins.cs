using ByteShelf.Server.Metadata.Models;
using ByteShelf.Server.Metadata.Services;

namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>The plugins a test says are installed. A test may set others, as a restart with others installed would.</summary>
    public sealed class TestMetadataPlugins : IMetadataPlugins
    {
        public TestMetadataPlugins(params MetadataPlugin[] plugins)
        {
            All = plugins;
        }

        public IReadOnlyList<MetadataPlugin> All { get; set; }
    }
}
