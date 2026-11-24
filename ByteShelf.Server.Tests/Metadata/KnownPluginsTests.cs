using ByteShelf.Server.Metadata.Services;
using ByteShelf.Server.Shared.Root;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Services;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;
using EasyReasy.Database;
using Microsoft.Extensions.DependencyInjection;

namespace ByteShelf.Server.Tests.Metadata
{
    /// <summary>What a root command's services may do with plugins' documents, as they know no plugins (<see cref="KnownPlugins.NotKnown"/>).</summary>
    public sealed class KnownPluginsTests : IAsyncLifetime
    {
        private ServerTestHost _host = null!;
        private ShelfFile _file = null!;

        public async Task InitializeAsync()
        {
            _host = await ServerTestHost.StartAsync(services => new TestMetadataPlugins(TestMetadataPlugins.Declaration).AddTo(services));
            await _host.CreateShelfAsync("docs");
            _file = await _host.PutAsAdminAsync("docs", "/a.txt", "a"u8.ToArray(), "text/plain");
            await _host.Services.SetFileDocumentAsync(_file.Id, "{\"caption\":\"kept\"}");
            await _host.Services.RecordContentDocumentAsync(_file.Sha256, "{\"words\":\"kept\"}");
        }

        public async Task DisposeAsync()
        {
            await _host.DisposeAsync();
        }

        /// <summary>A command does not know the plugins, so what it wrote would be found by none of its fields.</summary>
        [Fact]
        public async Task SetAndRecordAsync_OfACommand_AreRefusedAndChangeNothing()
        {
            await using ServiceProvider command = RootCommand.BuildServices(ServerSettings.Open(_host.TestRoot.Root), new RecordingLoggerFactory());

            await Assert.ThrowsAsync<InvalidOperationException>(() => command.SetFileDocumentAsync(_file.Id, "{\"caption\":\"new\"}"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => command.RecordContentDocumentAsync(_file.Sha256, "{\"words\":\"new\"}", version: 2));

            Assert.Equal("{\"caption\":\"kept\"}", await _host.Services.GetFileDocumentAsync(_file.Id));
            Assert.Equal("{\"words\":\"kept\"}", await _host.Services.GetContentDocumentAsync(_file.Sha256));

            // Undone with the transactions, as the values of a document are removed before it is refused.
            Assert.Equal(2, await CountValuesAsync());
        }

        [Fact]
        public async Task SetAsync_OfNoDocumentByACommand_RemovesItsValues()
        {
            await using ServiceProvider command = RootCommand.BuildServices(ServerSettings.Open(_host.TestRoot.Root), new RecordingLoggerFactory());

            await command.RemoveFileDocumentAsync(_file.Id);

            Assert.Null(await _host.Services.GetFileDocumentAsync(_file.Id));
            Assert.Equal(1, await CountValuesAsync());
        }

        /// <summary>Not as though there were none.</summary>
        [Fact]
        public async Task ListAsync_OfACommand_IsRefused()
        {
            await using ServiceProvider command = RootCommand.BuildServices(ServerSettings.Open(_host.TestRoot.Root), new RecordingLoggerFactory());
            using IServiceScope scope = command.CreateScope();
            Shelf shelf = await scope.ServiceProvider.GetRequiredService<IShelfService>().GetByNameAsync("docs");

            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IDirectoryService>().ListAsync(shelf, EntryReference.ForPath("/")));
        }

        /// <summary>How many values of the plugin's fields there are.</summary>
        private async Task<long> CountValuesAsync()
        {
            using IServiceScope scope = _host.Services.CreateScope();
            await using IDbSession session = await scope.ServiceProvider.GetRequiredService<IDbSessionFactory>().CreateSessionAsync();
            return await TestQueries.CountAsync(session, $"plugin_field_value WHERE plugin_id = '{TestPluginMetadata.Plugin}'");
        }
    }
}
