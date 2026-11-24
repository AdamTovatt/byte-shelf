using System.Data.Common;
using ByteShelf.Server.Metadata.Services;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;
using EasyReasy.Database;
using EasyReasy.Database.Mapping;
using Microsoft.Extensions.DependencyInjection;

namespace ByteShelf.Server.Tests.Metadata
{
    /// <summary>What a server start makes of the plugins installed then: their fields' values taken by what they declare now.</summary>
    public class PluginFieldIndexTests : ServiceTestBase
    {
        private const string Plugin = TestPluginMetadata.Plugin;

        [Fact]
        public async Task BringUpToDateAsync_PluginInstalledOverDocuments_TakesTheirValues()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");
            await Services.SetFileDocumentAsync(file.Id, "{\"rating\":4}");
            await Services.RecordContentDocumentAsync(file.Sha256, "{\"width\":10}");
            Assert.Empty(await ValuesAsync());

            Install(Field("rating", PluginFieldKind.Number), Field("width", PluginFieldKind.Number, PluginDocumentSubject.Contents));
            await Services.StartPluginFieldIndexAsync();

            Assert.Equal(["rating 4", "width 10"], await ValuesAsync());
        }

        [Fact]
        public async Task BringUpToDateAsync_MoreDocumentsThanABatch_TakesThemAll()
        {
            Shelf shelf = await CreateShelfAsync();
            for (int index = 0; index < PluginFieldIndex.BatchSize + 1; index++)
            {
                ShelfFile file = await StoreTextAsync(shelf, $"/{index}.txt", $"{index}");
                await Services.SetFileDocumentAsync(file.Id, "{\"rating\":4}");
                await Services.RecordContentDocumentAsync(file.Sha256, "{\"width\":10}");
            }

            Install(Field("rating", PluginFieldKind.Number), Field("width", PluginFieldKind.Number, PluginDocumentSubject.Contents));
            await Services.StartPluginFieldIndexAsync();

            Assert.Equal(2 * (PluginFieldIndex.BatchSize + 1), (await ValuesAsync()).Count);
        }

        [Fact]
        public async Task BringUpToDateAsync_OtherFieldsDeclared_TakesTheValuesAnew()
        {
            Install(Field("rating", PluginFieldKind.Number), Field("note", PluginFieldKind.Text));
            await Services.StartPluginFieldIndexAsync();
            ShelfFile file = await StoreTextAsync(await CreateShelfAsync(), "/a.txt", "a");
            await Services.SetFileDocumentAsync(file.Id, "{\"rating\":4,\"note\":\"Apple\",\"kind\":\"Fruit\"}");
            Assert.Equal(["note apple", "rating 4"], await ValuesAsync());

            Install(Field("kind", PluginFieldKind.Text), Field("note", PluginFieldKind.Text));
            await Services.StartPluginFieldIndexAsync();

            Assert.Equal(["kind fruit", "note apple"], await ValuesAsync());
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public async Task BringUpToDateAsync_FieldSearchedOrListedNow_IsSearchedOrListed(bool searched, bool listed)
        {
            Install(Field("note", PluginFieldKind.Text));
            await Services.StartPluginFieldIndexAsync();
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");
            await Services.SetFileDocumentAsync(file.Id, "{\"note\":\"apple\"}");

            Install(new PluginField("note", "Note", PluginFieldKind.Text, PluginDocumentSubject.File, searched, listed));
            await Services.StartPluginFieldIndexAsync();

            Assert.Equal(searched, (await FileService.SearchAsync(shelf, new FileSearch { Text = "apple" })).Files.Count == 1);
            Assert.Equal(listed, Assert.Single((await DirectoryService.ListAsync(shelf, EntryReference.ForPath("/"))).Files).PluginFields != null);
        }

        [Fact]
        public async Task BringUpToDateAsync_OnlyALabelChanged_LeavesTheValuesAsTheyAre()
        {
            Install(Field("rating", PluginFieldKind.Number));
            await Services.StartPluginFieldIndexAsync();
            ShelfFile file = await StoreTextAsync(await CreateShelfAsync(), "/a.txt", "a");
            await Services.SetFileDocumentAsync(file.Id, "{\"rating\":4}");
            await RunSqlAsync("UPDATE plugin_field_value SET number = 5");

            Install(new PluginField("rating", "Another label", PluginFieldKind.Number, PluginDocumentSubject.File));
            await Services.StartPluginFieldIndexAsync();

            Assert.Equal(["rating 5"], await ValuesAsync());
        }

        [Fact]
        public async Task BringUpToDateAsync_PluginGone_DropsItsValuesAndKeepsItsDocuments()
        {
            Install(Field("rating", PluginFieldKind.Number));
            await Services.StartPluginFieldIndexAsync();
            ShelfFile file = await StoreTextAsync(await CreateShelfAsync(), "/a.txt", "a");
            await Services.SetFileDocumentAsync(file.Id, "{\"rating\":4}");

            InstallPlugins();
            await Services.StartPluginFieldIndexAsync();
            Assert.Empty(await ValuesAsync());
            Assert.Equal("{\"rating\":4}", await Services.GetFileDocumentAsync(file.Id));

            Install(Field("rating", PluginFieldKind.Number));
            await Services.StartPluginFieldIndexAsync();
            Assert.Equal(["rating 4"], await ValuesAsync());
        }

        [Fact]
        public async Task BringUpToDateAsync_TwoPluginsOnOneFile_TakesOrDropsTheValuesOfTheOneThatChangedAlone()
        {
            PluginField[] fields = [Field("rating", PluginFieldKind.Number), Field("width", PluginFieldKind.Number, PluginDocumentSubject.Contents)];
            TestMetadataPlugin first = new TestMetadataPlugin(new PluginDeclaration(Plugin, "Test plugin", fields));
            TestMetadataPlugin second = new TestMetadataPlugin(new PluginDeclaration("second", "Second", fields));
            InstallPlugins(first, second);
            await Services.StartPluginFieldIndexAsync();
            ShelfFile file = await StoreTextAsync(await CreateShelfAsync(), "/a.txt", "a");
            foreach ((string plugin, int value) in new[] { (Plugin, 1), ("second", 2) })
            {
                await Services.SetFileDocumentAsync(file.Id, $"{{\"rating\":{value}}}", plugin);
                await Services.RecordContentDocumentAsync(file.Sha256, $"{{\"width\":{value}}}", plugin);
            }

            await Services.SetFileDocumentAsync(file.Id, "{\"rating\":3}", "second");
            await Services.RecordContentDocumentAsync(file.Sha256, "{\"width\":3}", "second", version: 2);
            Assert.Equal(["rating 1", "rating 3", "width 1", "width 3"], await ValuesAsync());

            InstallPlugins(first, new TestMetadataPlugin(new PluginDeclaration("second", "Second", [fields[0]])));
            await Services.StartPluginFieldIndexAsync();
            Assert.Equal(["rating 1", "rating 3", "width 1"], await ValuesAsync());

            InstallPlugins(first);
            await Services.StartPluginFieldIndexAsync();
            Assert.Equal(["rating 1", "width 1"], await ValuesAsync());
        }

        [Fact]
        public async Task Start_OfTheWholeServer_TakesTheValues()
        {
            await using ServerTestHost host = await ServerTestHost.StartAsync(services => new TestMetadataPlugins(TestMetadataPlugins.Declaration).AddTo(services));

            await using IDbSession session = await host.TestRoot.SessionFactory.CreateSessionAsync();
            Assert.Equal(1, await TestQueries.CountAsync(session, $"plugin_values_taken WHERE plugin_id = '{Plugin}'"));
        }

        [Fact]
        public async Task BringUpToDateAsync_ValuesTakenByOtherRulesOfTheServers_TakesThemAnew()
        {
            Install(Field("rating", PluginFieldKind.Number));
            await Services.StartPluginFieldIndexAsync();
            ShelfFile file = await StoreTextAsync(await CreateShelfAsync(), "/a.txt", "a");
            await Services.SetFileDocumentAsync(file.Id, "{\"rating\":4}");
            await RunSqlAsync(
                $"UPDATE plugin_field_value SET number = 5; UPDATE plugin_values_taken SET taken_by = replace(taken_by, '\"Version\":{PluginFieldIndex.Version}', '\"Version\":0')");

            await Services.StartPluginFieldIndexAsync();

            Assert.Equal(["rating 4"], await ValuesAsync());
        }

        [Fact]
        public async Task BringUpToDateAsync_StoppedHalfWay_IsTakenAnewByTheNextStart()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile first = await StoreTextAsync(shelf, "/a.txt", "a");
            ShelfFile second = await StoreTextAsync(shelf, "/b.txt", "b");
            await Services.RecordContentDocumentAsync(first.Sha256, "{\"width\":10}");
            await Services.SetFileDocumentAsync(second.Id, "{\"rating\":4}");
            Install(Field("rating", PluginFieldKind.Number), Field("width", PluginFieldKind.Number, PluginDocumentSubject.Contents));

            // Stopped once it has taken those about contents.
            await RunSqlAsync("CREATE TRIGGER stop BEFORE INSERT ON plugin_field_value WHEN new.field = 'rating' BEGIN SELECT RAISE(ABORT, 'stopped'); END");
            await Assert.ThrowsAnyAsync<DbException>(() => Services.StartPluginFieldIndexAsync());
            await RunSqlAsync("DROP TRIGGER stop");

            await Services.StartPluginFieldIndexAsync();

            Assert.Equal(["rating 4", "width 10"], await ValuesAsync());
        }

        [Fact]
        public async Task BringUpToDateAsync_LogsNothing()
        {
            ShelfFile file = await StoreTextAsync(await CreateShelfAsync(), "/a.txt", "a");
            await Services.SetFileDocumentAsync(file.Id, "{\"rating\":4}");
            long before = await ChangeLog.GetLatestSequenceAsync();

            Install(Field("rating", PluginFieldKind.Number));
            await Services.StartPluginFieldIndexAsync();

            Assert.Equal(["rating 4"], await ValuesAsync());
            Assert.Equal(before, await ChangeLog.GetLatestSequenceAsync());
        }

        private void Install(params PluginField[] fields)
        {
            InstallPlugins(new TestMetadataPlugin(new PluginDeclaration(Plugin, "Test plugin", fields)));
        }

        private static PluginField Field(string name, PluginFieldKind kind, PluginDocumentSubject document = PluginDocumentSubject.File)
        {
            return new PluginField(name, name, kind, document);
        }

        private Task<int> RunSqlAsync(string sql)
        {
            return Services.InTransactionAsync((_, session) => session.Connection.ExecuteAsync(sql, transaction: session.Transaction));
        }

        /// <summary>Every value in the table, with its field, in order.</summary>
        private async Task<List<string>> ValuesAsync()
        {
            await using IDbSession session = await Services.GetRequiredService<IDbSessionFactory>().CreateSessionAsync();
            return (await session.Connection.QueryAsync<string>(
                "SELECT field || ' ' || coalesce(text_fold, CAST(number AS INTEGER)) FROM plugin_field_value ORDER BY field, number", transaction: session.Transaction)).ToList();
        }
    }
}
