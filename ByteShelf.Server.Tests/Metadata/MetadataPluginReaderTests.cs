using System.Text.Json.Nodes;
using ByteShelf.Server.Metadata.Services;
using ByteShelf.Server.Tests.TestUtilities;

namespace ByteShelf.Server.Tests.Metadata
{
    public class MetadataPluginReaderTests
    {
        [Fact]
        public void Create_ReaderOfSomethingThatIsNoMediaType_IsRefused()
        {
            TestDocumentReader reader = new TestDocumentReader((_, _) => Task.FromResult<JsonObject?>(null), mediaTypes: ["image/png", "*.png"]);

            Assert.Throws<ArgumentException>(() => new MetadataPluginReader(reader));
        }
    }
}
