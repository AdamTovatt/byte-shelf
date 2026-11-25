using System.Diagnostics;
using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Derived.Services;
using ByteShelf.Server.Metadata.Models;
using ByteShelf.Server.Metadata.Repositories;
using ByteShelf.Server.Metadata.Services;
using ByteShelf.Server.Plugins.Models;
using ByteShelf.Server.Plugins.Processes;
using ByteShelf.Server.Plugins.Services;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Repositories;
using ByteShelf.Server.Tests.Derived;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;
using EasyReasy.Database;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ByteShelf.Server.Tests.Metadata
{
    /// <summary>A plugin's reader in its real process: what its failures come to, and what a swap or a purge of what it reads does to the pass.</summary>
    public sealed class PluginReadingProcessTests : DerivedTestBase
    {
        private static readonly TimeSpan RunLimit = TimeSpan.FromSeconds(60);

        protected override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);
            services.AddSingleton<IPluginCatalog>(new OneReader());
            services.Replace(ServiceDescriptor.Singleton<IPluginReaders, LoadedPluginReaders>());
        }

        [Theory]
        [InlineData("throw secret")]
        [InlineData("crash secret")]
        [InlineData("long")]
        [InlineData("large")]
        public async Task RunAsync_AReadThatFails_IsRecordedAsNothingOnceAndStopsNoOther(string contents)
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile bad = await StoreTextAsync(shelf, "/bad.txt", contents);
            ShelfFile good = await StoreTextAsync(shelf, "/good.txt", "good");

            PluginMetadataReadingSummary first = await RunAsync();
            PluginMetadataReadingSummary second = await RunAsync();

            Assert.Equal((2, 1, 0), (first.Read, first.Failed, second.Read));
            Assert.Null((await RowAsync(bad))!.Document);
            Assert.Equal("""{"text":"good"}""", (await RowAsync(good))!.Document!.Json);
        }

        [Fact]
        public async Task RunAsync_AReadOverItsTime_IsRecordedAsNothing()
        {
            ShelfFile file = await StoreTextAsync(await CreateShelfAsync(), "/a.txt", Plugins.Waiting("a").Replace("wait", "hang", StringComparison.Ordinal));
            Task<PluginMetadataReadingSummary> run = RunAsync();
            using Process process = await Plugins.StartedAsync("a");
            await Clock.WaitForTimerAsync(RunLimit);

            Clock.Advance(RunLimit);

            Assert.Equal(1, (await run).Failed);
            Assert.True(process.HasExited);
            Assert.Null((await RowAsync(file))!.Document);
        }

        [Fact]
        public async Task RunAsync_NothingFound_IsNoFailure()
        {
            ShelfFile file = await StoreTextAsync(await CreateShelfAsync(), "/a.txt", "nothing");

            PluginMetadataReadingSummary summary = await RunAsync();

            Assert.Equal((1, 0), (summary.Read, summary.Failed));
            Assert.Null((await RowAsync(file))!.Document);
        }

        [Fact]
        public async Task RunAsync_ContentsThatAreGone_AreLeftUnread()
        {
            ShelfFile file = await StoreTextAsync(await CreateShelfAsync(), "/a.txt", "a");
            ObjectStore.Delete(file.Sha256);

            Assert.Equal(0, (await RunAsync()).Read);

            Assert.Null(await RowAsync(file));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task RunAsync_FileEncryptedInPlaceWhileItIsRead_EndsTheProcessAndReadsTheRest(bool encryptedCopyRemains)
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile[] files = await StoreTwoAsync(shelf);
            if (encryptedCopyRemains)
            {
                await StoreAsync(shelf, "/copy.age", AgeBytes, "text/plain", encrypted: true);
            }

            Task<PluginMetadataReadingSummary> run = RunAsync();
            using Process process = await Plugins.StartedAsync("a");

            await EncryptInPlaceAsync(shelf, files[0]);

            Assert.True(process.HasExited);
            Assert.Equal((1, 0), Counts(await run));
            Assert.Null(await RowAsync(files[0]));
            Assert.NotNull(await RowAsync(files[1]));
            Assert.DoesNotContain(files[0].Sha256, Processes.GetContentsGiven());
            await using IDbSession session = await Services.GetRequiredService<IDbSessionFactory>().CreateSessionAsync();
            Assert.Equal(0, await TestQueries.CountAsync(session, "change_log WHERE instr(payload, 'wait ') > 0"));
        }

        [Fact]
        public async Task RunAsync_FilePurgedWhileItIsRead_EndsTheProcessAndReadsTheRest()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile[] files = await StoreTwoAsync(shelf);
            Task<PluginMetadataReadingSummary> run = RunAsync();
            using Process process = await Plugins.StartedAsync("a");

            await FileService.DeleteFileAsync(shelf, EntryReference.ForId(files[0].Id));
            await TrashService.EmptyAsync(shelf, CancellationToken.None).WaitAsync(RealTimeLimit);

            Assert.True(process.HasExited);
            Assert.Equal((1, 0), Counts(await run));
            Assert.Null(await RowAsync(files[0]));
            Assert.NotNull(await RowAsync(files[1]));
        }

        [Fact]
        public async Task RunAsync_ServerStopping_RecordsNothing()
        {
            ShelfFile file = await StoreTextAsync(await CreateShelfAsync(), "/a.txt", Plugins.Waiting("a"));
            Task<PluginMetadataReadingSummary> run = RunAsync();
            using Process process = await Plugins.StartedAsync("a");

            await Processes.StopAllAsync().WaitAsync(RealTimeLimit);

            Assert.True(process.HasExited);
            Assert.Equal((0, 0), Counts(await run));
            Assert.Null(await RowAsync(file));
        }

        [Fact]
        public async Task Maintenance_OfContentsOnlyAReaderReadWhoseLastFileWentUnnoticed_EndsItsProcess()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", Plugins.Waiting("a"));
            ShelfFile left = await StoreTextAsync(shelf, "/b.csv", "left by an earlier server");
            ShelfFile held = await StoreTextAsync(shelf, "/c.csv", "held");
            Task<PluginMetadataReadingSummary> run = RunAsync();
            using Process process = await Plugins.StartedAsync("a");
            Plugins.GoOn("a");
            await run;
            WorkFolder leftFolder = WorkFolder.Create(TestRoot.Root);
            leftFolder.Add(left.Sha256);
            WorkFolder heldFolder = WorkFolder.Create(TestRoot.Root);
            heldFolder.Add(held.Sha256);
            foreach (ShelfFile gone in new[] { file, left })
            {
                await Services.InTransactionAsync((provider, session) =>
                    provider.GetRequiredService<IFileRepository>().PurgeAllWithObjectAsync(gone.Sha256, TestInstants.Now, session));
            }

            await using AsyncServiceScope scope = Services.CreateAsyncScope();
            DerivedMaintenanceSummary summary = await scope.ServiceProvider.GetRequiredService<DerivedMaintenance>().RunAsync(CancellationToken.None).WaitAsync(RealTimeLimit);

            Assert.Equal(new DerivedMaintenanceSummary(Unheld: 2, Orphaned: 0, Trimmed: 0, Recounted: 0), summary);
            Assert.True(process.HasExited);
            Assert.Equal(heldFolder.Path, Assert.Single(WorkFolder.Find(TestRoot.Root)).Path);
        }

        private Task<PluginMetadataReadingSummary> RunAsync()
        {
            return Services.ReadPluginMetadataAsync().WaitAsync(RealTimeLimit);
        }

        /// <summary>Two files in the order they are read, of which the first is read for as long as the test says.</summary>
        private async Task<ShelfFile[]> StoreTwoAsync(Shelf shelf)
        {
            ShelfFile first = await StoreTextAsync(shelf, "/first.txt", Plugins.Waiting("a"));
            for (int text = 0; ; text++)
            {
                ShelfFile next = await StoreTextAsync(shelf, $"/{text}.txt", $"{text}");
                if (string.CompareOrdinal(first.Sha256, next.Sha256) < 0)
                {
                    return [first, next];
                }

                await FileService.DeleteFileAsync(shelf, EntryReference.ForId(next.Id));
            }
        }

        private Task<ContentPluginMetadata?> RowAsync(ShelfFile file)
        {
            return Services.GetRequiredService<IContentPluginMetadataRepository>().GetAsync(TestPluginMetadata.Plugin, file.Sha256);
        }

        private static (int Read, int Failed) Counts(PluginMetadataReadingSummary summary)
        {
            return (summary.Read, summary.Failed);
        }

        private sealed class OneReader : IPluginCatalog
        {
            public IReadOnlyList<LoadedPlugin> Loaded { get; } = [TestReader.Plugin(TestPluginMetadata.Plugin)];

            public IReadOnlyList<SkippedPlugin> Skipped => [];
        }
    }
}
