using ByteShelf.Server.Metadata.Services;
using ByteShelf.Server.Plugins.Processes;
using ByteShelf.Server.Tests.TestUtilities;

namespace ByteShelf.Server.Tests.Metadata
{
    /// <summary>What the runs of a reader come to that no file on disk can bring about.</summary>
    public sealed class ReadRunTests
    {
        [Fact]
        public async Task TakeAsync_ContentsTheServerCouldNotRead_AreNotRead()
        {
            ReadRun run = new ReadRun(new PluginTarget(TestPluginMetadata.Plugin, string.Empty), "a", 1);

            PluginReading reading = await run.TakeAsync(new PluginRunResult.ContentsNotRead(), null!, CancellationToken.None);

            Assert.IsType<PluginReading.NotRead>(reading);
        }
    }
}
