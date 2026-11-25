using System.Text.Json;
using System.Text.Json.Nodes;
using ByteShelf.Plugins;
using ByteShelf.Server.Derived.Services;
using ByteShelf.Server.Metadata.Services;
using ByteShelf.Server.Plugins.Models;
using ByteShelf.Server.Plugins.Processes;
using ByteShelf.Server.Storage.Models;

namespace ByteShelf.Server.Tests.Plugins.Processes
{
    /// <summary>What a plugin's process says it cannot do, whatever the contents: what admins are told.</summary>
    public sealed class PluginWorksTests
    {
        private static readonly JsonElement NoArguments = JsonSerializer.SerializeToElement(new { });

        [Fact]
        public void Named_AWorkNoModuleRegistered_IsUnavailable()
        {
            PluginWorks works = PluginWorks.Of([new ReadWork()], new PluginRegistrations(null, null));

            PluginUnavailableException refused = Assert.Throws<PluginUnavailableException>(() => works.Named(ProcessWork.NameOf(typeof(MakeWork))));

            Assert.Equal("it does not do 'MakeWork'", refused.Message);
        }

        [Fact]
        public async Task Read_ByAPluginWithNoReader_IsUnavailable()
        {
            PluginWorks works = PluginWorks.Of([new ReadWork()], new PluginRegistrations(null, null));

            PluginUnavailableException refused = await Assert.ThrowsAsync<PluginUnavailableException>(() => RunAsync(works, typeof(ReadWork)));

            Assert.Equal("it has no reader", refused.Message);
        }

        [Fact]
        public async Task Make_ByAPluginWithNoMaker_IsUnavailable()
        {
            PluginWorks works = PluginWorks.Of([new MakeWork()], new PluginRegistrations(null, null));

            PluginUnavailableException refused = await Assert.ThrowsAsync<PluginUnavailableException>(() => RunAsync(works, typeof(MakeWork)));

            Assert.Equal("it has no maker", refused.Message);
        }

        [Fact]
        public async Task Make_ByAMakerThatCannotBeMade_IsUnavailableSayingWhy()
        {
            PluginMakerRegistration maker = new PluginMakerRegistration(() => throw new InvalidOperationException("It was told to fail."), []);
            PluginWorks works = PluginWorks.Of([new MakeWork()], new PluginRegistrations(null, maker));

            PluginUnavailableException refused = await Assert.ThrowsAsync<PluginUnavailableException>(() => RunAsync(works, typeof(MakeWork)));

            Assert.Equal("its maker could not be made: It was told to fail", refused.Message);
        }

        [Fact]
        public void Of_AMakerThatCannotBeMade_MakesTheReaderAllTheSame()
        {
            int made = 0;
            PluginReaderRegistration reader = new PluginReaderRegistration(() => { made++; return new NoDocuments(); }, 1, MediaTypeSet.Of("text/plain"));
            PluginMakerRegistration maker = new PluginMakerRegistration(() => throw new InvalidOperationException("It was told to fail."), []);

            PluginWorks.Of([new ReadWork(), new MakeWork()], new PluginRegistrations(reader, maker));

            Assert.Equal(1, made);
        }

        private static Task<JsonElement> RunAsync(PluginWorks works, Type work)
        {
            return works.Named(ProcessWork.NameOf(work))(NoArguments, Stream.Null, Path.GetTempPath());
        }

        private sealed class NoDocuments : IContentDocumentReader
        {
            public Task<JsonObject?> ReadAsync(Stream contents)
            {
                return Task.FromResult<JsonObject?>(null);
            }
        }
    }
}
