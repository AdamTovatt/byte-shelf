using ByteShelf.Server.ChangeLog.Models;
using ByteShelf.Server.Metadata.Services;
using ByteShelf.Server.Shared.Root;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;
using EasyReasy.Database;
using Microsoft.Extensions.DependencyInjection;

namespace ByteShelf.Server.Tests.Metadata
{
    /// <summary><c>--forget-plugin</c>, run against the root of a server running in memory.</summary>
    public sealed class PluginMetadataCommandTests : IAsyncLifetime
    {
        private readonly RecordingLoggerFactory _logs = new RecordingLoggerFactory();
        private ServerTestHost _host = null!;
        private ShelfFile _file = null!;

        public async Task InitializeAsync()
        {
            _host = await ServerTestHost.StartAsync(services => new TestMetadataPlugins(TestMetadataPlugins.Declaration).AddTo(services));
            await _host.CreateShelfAsync("docs");
            _file = await _host.PutAsAdminAsync("docs", "/a.txt", "a"u8.ToArray(), "text/plain");
            foreach (string plugin in new[] { TestPluginMetadata.Plugin, "another" })
            {
                await _host.Services.SetFileDocumentAsync(_file.Id, "{\"caption\":\"to forget\"}", plugin);
                await _host.Services.RecordContentDocumentAsync(_file.Sha256, "{\"words\":\"to forget\"}", plugin);
            }

            ShelfFile removed = await _host.PutAsAdminAsync("docs", "/b.txt", "b"u8.ToArray(), "text/plain");
            await _host.Services.SetFileDocumentAsync(removed.Id, "{}");
            await _host.Services.RemoveFileDocumentAsync(removed.Id);
        }

        public async Task DisposeAsync()
        {
            await _host.DisposeAsync();
        }

        [Fact]
        public async Task ForgetPlugin_DeletesEverythingItKeepsAndLogsItAndLeavesOthers()
        {
            Assert.Equal(RootCommand.Done, await RunAsync("--forget-plugin", TestPluginMetadata.Plugin));

            Assert.Equal("Deleted everything the plugin 'test-plugin' kept: about 1 files' contents and 2 files.", _logs.Messages[^1]);
            using IServiceScope scope = _host.Services.CreateScope();
            await using IDbSession session = await scope.ServiceProvider.GetRequiredService<IDbSessionFactory>().CreateSessionAsync();
            foreach (string table in new[] { "content_plugin_metadata", "file_plugin_metadata", "plugin_field_value" })
            {
                Assert.Equal(0, await TestQueries.CountAsync(session, $"{table} WHERE plugin_id = '{TestPluginMetadata.Plugin}'"));
            }

            Assert.Equal(1, await TestQueries.CountAsync(session, "content_plugin_metadata"));
            Assert.Equal(1, await TestQueries.CountAsync(session, "file_plugin_metadata"));
            IReadOnlyList<ChangeLogEntry> states = await _host.Services.GetLatestStatesAsync();
            Assert.Equal(2, states.Count(state => state.EntityType == ChangeEntityType.FilePluginMetadata && state.Payload == null));
            Assert.Single(states, state => state.EntityType == ChangeEntityType.ContentPluginMetadata && state.Payload == null);

            // The log forgets both kinds; it keeps "another"'s.
            foreach (string type in new[] { ChangeEntityType.ContentPluginMetadata, ChangeEntityType.FilePluginMetadata })
            {
                Assert.Equal(1, await TestQueries.CountAsync(session, $"change_log WHERE entity_type = '{type}' AND payload IS NOT NULL"));
            }
        }

        [Fact]
        public async Task ForgetPlugin_ThatKeptNothing_SaysSo()
        {
            Assert.Equal(RootCommand.Done, await RunAsync("--forget-plugin", "never-installed"));

            Assert.Contains("kept nothing", _logs.Messages[^1], StringComparison.Ordinal);
        }

        [Fact]
        public async Task ForgetPlugin_OnAReplica_IsRefused()
        {
            await TestReplicas.MarkAsReplicaAsync(_host.Services);

            Assert.Equal(RootCommand.Failed, await RunAsync("--forget-plugin", TestPluginMetadata.Plugin));

            await AssertKeptAsync();
        }

        [Theory]
        [InlineData("--forget-plugin")]
        [InlineData("--forget-plugin", TestPluginMetadata.Plugin, "b")]
        [InlineData("--forget-plugin", TestPluginMetadata.Plugin, "--promote")]
        public async Task ForgetPlugin_OtherArguments_AreAUsageError(params string[] args)
        {
            Assert.Equal(RootCommand.Usage, await RunAsync(args));

            await AssertKeptAsync();
        }

        private async Task AssertKeptAsync()
        {
            Assert.NotNull(await _host.Services.GetFileDocumentAsync(_file.Id));
            Assert.NotNull(await _host.Services.GetContentDocumentAsync(_file.Sha256));
        }

        private Task<int> RunAsync(params string[] args)
        {
            return PluginMetadataCommand.RunAsync(args, _host.TestRoot.Root.Path, _logs);
        }
    }
}
