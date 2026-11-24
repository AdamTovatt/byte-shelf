using System.Text.Json.Nodes;
using ByteShelf.Plugins;
using ByteShelf.Server.Metadata.Models;
using ByteShelf.Server.Metadata.Services;
using ByteShelf.Server.Plugins.Models;
using ByteShelf.Server.Plugins.Services;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.Plugins
{
    /// <summary>When the readers of loaded plugins are made: their constructors are the plugins' code.</summary>
    public class LoadedPluginReadersTests
    {
        private static readonly TimeSpan RealTimeLimit = TimeSpan.FromSeconds(30);

        [Fact]
        public async Task All_AsksForNoReaderToBeMade_AndEachIsMadeOnce()
        {
            int made = 0;
            TestDocumentReader reader = TestDocumentReader.OfText();
            PluginReader found = Assert.Single(ReadersOf(() =>
            {
                made++;
                return reader;
            }).All);
            Assert.Equal(("test", 3), (found.PluginId, found.Version));

            // Long enough for one made in the background to have been.
            Assert.False(SpinWait.SpinUntil(() => Volatile.Read(ref made) > 0, TimeSpan.FromMilliseconds(500)));

            Assert.Same(reader, await found.GetAsync());
            Assert.Same(reader, await found.GetAsync());

            Assert.Equal(1, made);
        }

        [Fact]
        public async Task GetAsync_ConstructorThatHangs_LetsTheCallerGoOn()
        {
            using ManualResetEventSlim release = new ManualResetEventSlim();
            PluginReader reader = Assert.Single(ReadersOf(() =>
            {
                release.Wait(RealTimeLimit);
                return TestDocumentReader.OfText();
            }).All);

            Task<IContentDocumentReader> making = reader.GetAsync();
            Assert.False(making.IsCompleted);

            release.Set();
            await making.WaitAsync(RealTimeLimit);
        }

        [Fact]
        public async Task GetAsync_ConstructorThatFails_FailsAsItDidWhereverItDid()
        {
            PluginReader reader = Assert.Single(ReadersOf(() => new Broken()).All);

            DllNotFoundException exception = await Assert.ThrowsAsync<DllNotFoundException>(() => reader.GetAsync());

            Assert.Contains(nameof(Broken), exception.StackTrace, StringComparison.Ordinal);
        }

        [Fact]
        public void All_PluginWithoutAReader_HasNone()
        {
            Assert.Empty(new LoadedPluginReaders(new PluginCatalog([Plugin(null)], [])).All);
        }

        private static LoadedPluginReaders ReadersOf(Func<IContentDocumentReader> make)
        {
            return new LoadedPluginReaders(new PluginCatalog([Plugin(new PluginReaderRegistration(make, 3, MediaTypeSet.Of("text/plain")))], []));
        }

        private static LoadedPlugin Plugin(PluginReaderRegistration? reader)
        {
            return new LoadedPlugin(new PluginManifest(new PluginDeclaration("test", "Test", []), "1", null), null, reader);
        }

        private sealed class Broken : IContentDocumentReader
        {
            private static readonly int Library = Load();

            private readonly int _library = Library;

            public Task<JsonObject?> ReadAsync(Stream contents, CancellationToken cancellationToken)
            {
                throw new InvalidOperationException($"It has {_library}.");
            }

            private static int Load()
            {
                throw new DllNotFoundException("There is no libimages.so.");
            }
        }
    }
}
