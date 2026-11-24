using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Storage.Repositories;
using ByteShelf.Server.Derived.Services;
using ByteShelf.Server.Plugins.Processes;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;
using Microsoft.Extensions.DependencyInjection;

namespace ByteShelf.Server.Tests.Derived
{
    /// <summary>What is made without anybody asking, and what maintenance removes.</summary>
    public sealed class DerivedBackgroundTests : DerivedTestBase
    {
        [Fact]
        public async Task MakeBacklogAsync_MakesThumbnailsAndPreviewsOfLivePlainFilesOnce()
        {
            Shelf shelf = await CreateShelfAsync();
            await StoreTextAsync(shelf, "/a.txt", "a");
            await StoreTextAsync(shelf, "/copy.txt", "a");
            await StoreTextAsync(shelf, "/throw.txt", "throw");
            await StoreAsync(shelf, "/encrypted.txt", AgeBytes, "text/plain", encrypted: true);
            await StoreAsync(shelf, "/other.bin", [1], "application/octet-stream");
            ShelfFile trashed = await StoreTextAsync(shelf, "/trashed.txt", "trashed");
            await FileService.DeleteFileAsync(shelf, EntryReference.ForId(trashed.Id));

            Assert.Equal((2, 0), (await MakeBacklogAsync(), await MakeBacklogAsync()));

            Assert.Equal(["a: Thumbnail, Preview", "throw: Thumbnail, Preview"], Plugins.Runs.Order(StringComparer.Ordinal));
        }

        [Fact]
        public async Task MakeBacklogAsync_MoreThanABatch_AreAllMade()
        {
            // Failures, as they take no child process of their own to be known already; by SHA-256 all before the last.
            Shelf shelf = await CreateShelfAsync();
            string last = Enumerable.Range(0, int.MaxValue).Select(number => $"missing {number}").First(text => TestHashes.Sha256Of(text).StartsWith("fff", StringComparison.Ordinal));
            await StoreTextAsync(shelf, "/last.txt", last);
            string[] known = Enumerable.Range(0, int.MaxValue)
                .Select(number => $"known {number}")
                .Where(text => string.CompareOrdinal(TestHashes.Sha256Of(text), TestHashes.Sha256Of(last)) < 0)
                .Take(DerivedBackground.BatchSize + 1)
                .ToArray();
            foreach (string text in known)
            {
                ShelfFile file = await StoreTextAsync(shelf, $"/{text}.txt", text);
                foreach (DerivedKind kind in DerivedKinds.In(MakingGroup.Background))
                {
                    await PutAsync(file.Sha256, new KeptFile(kind, new DerivedEntry(TestMaker.Spec.DefaultId, 1, new DerivedOutcome.Failed()), SourcePath: null));
                }
            }

            Assert.Equal(1, await MakeBacklogAsync());
            Assert.Equal([$"{last}: Thumbnail, Preview"], Plugins.Runs);
        }

        [Fact]
        public async Task MakeStoredAsync_WhileABacklogIsMade_GoesAheadOfTheRestOfIt()
        {
            Shelf shelf = await CreateShelfAsync();
            string[] contents = [Plugins.Waiting("0"), .. Enumerable.Range(1, 3).Select(number => $"backlog {number}")];
            foreach (string text in contents)
            {
                await StoreTextAsync(shelf, $"/{TestHashes.Sha256Of(text)}.txt", text);
            }

            // The backlog goes by SHA-256: the run that waits holds it up from its own place on.
            Task<int> backlog = MakeBacklogAsync();
            using (await Plugins.StartedAsync("0"))
            {
                int before = Plugins.Runs.Count;
                ShelfFile stored = await StoreTextAsync(shelf, "/stored.txt", "stored");
                await using AsyncServiceScope scope = Services.CreateAsyncScope();
                Task making = scope.ServiceProvider.GetRequiredService<DerivedBackground>().MakeStoredAsync(Plugins.All[0], stored.Sha256, CancellationToken.None);
                Plugins.GoOn("0");
                await Task.WhenAll(backlog, making);

                Assert.Equal("stored: Thumbnail, Preview", Plugins.Runs[before]);
            }
        }

        [Fact]
        public async Task MakeBacklogAsync_AnotherVersionOrPlugin_MakesThemAgain()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");
            await MakeBacklogAsync();

            Plugins.All = [Plugins.Plugin(version: 2)];
            Assert.Equal(1, await MakeBacklogAsync());
            Plugins.All = [Plugins.Plugin("b", 1, null, "text/*"), Plugins.Plugin("c")];
            Assert.Equal(1, await MakeBacklogAsync());

            Assert.Equal("c", Store.Find(DerivedKind.Thumbnail, file.Sha256)!.Plugin);
        }

        [Fact]
        public async Task MakeBacklogAsync_WhatTwoPluginsMakeBetweenThem_IsMadeByEach()
        {
            Plugins.All = [Plugins.Plugin("a", 1, [DerivedKind.Thumbnail]), Plugins.Plugin("b", 1, [DerivedKind.Thumbnail, DerivedKind.Preview])];
            Shelf shelf = await CreateShelfAsync();
            await StoreTextAsync(shelf, "/a.txt", "text");

            Assert.Equal((2, 0), (await MakeBacklogAsync(), await MakeBacklogAsync()));

            Assert.Equal(["text: Preview", "text: Thumbnail"], Plugins.Runs.Order(StringComparer.Ordinal));
        }

        [Fact]
        public async Task MakeBacklogAsync_APluginThatCannotRun_EndsItsPass()
        {
            DerivedFilePlugin installed = Plugins.Plugin();
            Plugins.All = [installed with { Target = new PluginTarget(installed.Id, string.Empty) }];
            Shelf shelf = await CreateShelfAsync();
            await StoreTextAsync(shelf, "/a.txt", "a");

            await Assert.ThrowsAsync<PluginUnavailableException>(MakeBacklogAsync);
        }

        [Fact]
        public async Task MakeBacklogAsync_WhatAnotherPluginMakesByATypeOnlyTheTrashHoldsItAs_IsLeftToThat()
        {
            Plugins.All = [Plugins.Plugin("a", 1, null, "application/json"), Plugins.Plugin("b", 1, null, "text/plain")];
            Shelf shelf = await CreateShelfAsync();
            await StoreAsync(shelf, "/a.txt", "1"u8.ToArray(), "text/plain");
            ShelfFile json = await StoreAsync(shelf, "/a.json", "1"u8.ToArray(), "application/json");
            await FileService.DeleteFileAsync(shelf, EntryReference.ForId(json.Id));

            // Each with every type, so it is not even asked to be made.
            (string sha256, IReadOnlyList<string> types) = Assert.Single(
                await Services.GetRequiredService<IPlainContentsRepository>().GetLiveAsync(MediaTypeSet.Of("text/plain"), after: null, limit: 10));
            Assert.Equal(json.Sha256, sha256);
            Assert.Equal(["application/json", "text/plain"], types);
            Assert.Equal(0, await MakeBacklogAsync());
        }

        [UnixFact]
        public async Task MakeBacklogAsync_ContentsThatCannotBeOpened_DoNotKeepTheRestFromBeingMade()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile[] files = [await StoreTextAsync(shelf, "/a.txt", "a"), await StoreTextAsync(shelf, "/b.txt", "b")];
            ShelfFile first = files.MinBy(file => file.Sha256, StringComparer.Ordinal)!;
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(ObjectStore.GetPath(first.Sha256), UnixFileMode.None);
            }

            Assert.Equal(1, await MakeBacklogAsync());
        }

        [Fact]
        public async Task DerivedMaking_AsItStarts_MakesWhatIsMissingOfFilesAlreadyStored()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");
            using DerivedMakingWorker worker = ActivatorUtilities.CreateInstance<DerivedMakingWorker>(Services);

            await worker.StartAsync(CancellationToken.None);
            await TestDerivedPlugins.UntilAsync(() => Store.Find(DerivedKind.Preview, file.Sha256) != null);
            await worker.StopAsync(CancellationToken.None);

            AssertKept((DerivedKind.Thumbnail, file), (DerivedKind.Preview, file));
        }

        [Fact]
        public async Task Maintenance_WhatNoPlainFileHolds_IsRemoved()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile kept = await StoreTextAsync(shelf, "/kept.txt", "kept");
            await GetAsync(shelf, kept);
            await PutAsync(TestHashes.Sha256Of("gone"), new KeptFile(DerivedKind.Thumbnail, new DerivedEntry(TestMaker.Spec.DefaultId, 1, new DerivedOutcome.Failed()), SourcePath: null));

            Assert.Equal(new DerivedMaintenanceSummary(Unheld: 1, Orphaned: 0, Trimmed: 0, Recounted: 0), await MaintainAsync());

            AssertKept((DerivedKind.Thumbnail, kept), (DerivedKind.Preview, kept));
        }

        [Fact]
        public async Task Maintenance_WhatACrashLeftWithoutItsEntry_IsRemoved()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");
            string left = Store.GetPath(DerivedKind.Full, file.Sha256);
            Directory.CreateDirectory(Path.GetDirectoryName(left)!);
            await File.WriteAllTextAsync(left, "left by a crash before its entry was written");

            Assert.Equal(new DerivedMaintenanceSummary(Unheld: 0, Orphaned: 1, Trimmed: 0, Recounted: 0), await MaintainAsync());

            Assert.Empty(AllDerivedFiles());
        }

        [Fact]
        public async Task Maintenance_WhatNoInstalledPluginMakes_IsRemoved()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");
            await GetAsync(shelf, file);
            await GetAsync(shelf, file, DerivedKind.Agent);

            // Another version's stays until it is made again.
            Plugins.All = [Plugins.Plugin(version: 2, kinds: [DerivedKind.Agent])];
            Assert.Equal(new DerivedMaintenanceSummary(Unheld: 0, Orphaned: 2, Trimmed: 0, Recounted: 0), await MaintainAsync());
            AssertKept((DerivedKind.Agent, file));

            Plugins.All = [];
            Assert.Equal(new DerivedMaintenanceSummary(Unheld: 0, Orphaned: 1, Trimmed: 0, Recounted: 0), await MaintainAsync());
            AssertKept();
        }

        [Fact]
        public async Task Maintenance_FullFilesOverTheirLimit_AreTrimmed()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile[] files = [await StoreTextAsync(shelf, "/a.txt", "a"), await StoreTextAsync(shelf, "/b.txt", "b")];
            foreach (ShelfFile file in files)
            {
                await PutLargeFullAsync(file);
                Clock.Advance(TimeSpan.FromMinutes(1));
            }

            Assert.Equal(new DerivedMaintenanceSummary(Unheld: 0, Orphaned: 0, Trimmed: 1, Recounted: 0), await MaintainAsync());

            AssertKept((DerivedKind.Full, files[1]));
        }

        private async Task<DerivedMaintenanceSummary> MaintainAsync()
        {
            await using AsyncServiceScope scope = Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<DerivedMaintenance>().RunAsync(CancellationToken.None);
        }
    }
}
