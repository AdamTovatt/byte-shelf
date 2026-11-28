using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Derived.Services;
using ByteShelf.Server.Plugins.Models;
using ByteShelf.Server.Plugins.Processes;
using ByteShelf.Server.Plugins.Services;
using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;
using Microsoft.Extensions.DependencyInjection;

namespace ByteShelf.Server.Tests.Derived
{
    /// <summary>
    /// Streams (<see cref="DerivedKind.Stream"/>), which only the background makes, in a group and a job of their own:
    /// asked for, one that is missing is never made.
    /// </summary>
    public sealed class DerivedStreamTests : DerivedTestBase
    {
        // Written out rather than taken from the code, so a change of it is noticed.
        private static readonly TimeSpan BackgroundRetryAfter = TimeSpan.FromMinutes(1);

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            Plugins.All = [Plugins.Plugin(kinds: [DerivedKind.Thumbnail, DerivedKind.Preview, DerivedKind.Stream])];
        }

        [Fact]
        public async Task GetAsync_AStreamNotYetMade_IsLaterAtOnceAndMakesNothing()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");

            RetryLaterException later = await Assert.ThrowsAsync<RetryLaterException>(() => GetAsync(shelf, file, DerivedKind.Stream));

            Assert.Equal(BackgroundRetryAfter, later.RetryAfter);
            Assert.Empty(Plugins.Runs);
            Assert.Null(Store.Find(DerivedKind.Stream, file.Sha256));
        }

        [Fact]
        public async Task GetDeletedAsync_AStreamNotYetMadeOfContentsOnlyTheTrashHolds_IsNone()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");
            TrashEntry deleted = await FileService.DeleteFileAsync(shelf, EntryReference.ForId(file.Id));

            await Assert.ThrowsAsync<NotFoundException>(() => DerivedFiles.GetDeletedAsync(shelf, deleted.DeletionId, file.Id, DerivedKind.Stream, CancellationToken.None));

            // The background makes streams only of what live files hold: none is to come.
            Assert.Equal(0, await MakeBacklogAsync(MakingGroup.Stream));
            Assert.Empty(Plugins.Runs);
        }

        /// <summary>
        /// The background makes it by the live files of the types its maker takes, which a live file of another type with
        /// the same contents is not: it asks no shelf of its own.
        /// </summary>
        [Fact]
        public async Task GetDeletedAsync_AStreamOfContentsOnlyATrashedFileOfItsTypeHolds_IsNone()
        {
            Plugins.All = [Plugins.Plugin(kinds: [DerivedKind.Stream], mediaTypes: "video/mp4")];
            Shelf shelf = await CreateShelfAsync();
            ShelfFile video = await StoreAsync(shelf, "/a.mp4", "a"u8.ToArray(), "video/mp4");
            await StoreTextAsync(shelf, "/a.txt", "a");
            TrashEntry deleted = await FileService.DeleteFileAsync(shelf, EntryReference.ForId(video.Id));

            await Assert.ThrowsAsync<NotFoundException>(() => DerivedFiles.GetDeletedAsync(shelf, deleted.DeletionId, video.Id, DerivedKind.Stream, CancellationToken.None));

            Assert.Equal(0, await MakeBacklogAsync(MakingGroup.Stream));
        }

        /// <summary>Neither to be made nor being made, it is the plugin that cannot run, as any kind's is, until a later long run of its begins.</summary>
        [Fact]
        public async Task GetAsync_AStreamWhoseLongRunsCouldNotRun_IsThePluginUnavailableUntilOneBegins()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/full.txt", "full disk");
            await Assert.ThrowsAsync<PluginUnavailableException>(() => MakeAsync(file, DerivedKind.Stream));

            await Assert.ThrowsAsync<PluginUnavailableException>(() => GetAsync(shelf, file, DerivedKind.Stream));

            ShelfFile later = await StoreTextAsync(shelf, "/later.txt", Plugins.Waiting("later", DerivedKind.Stream));
            Task<MakingOutcome> making = MakeAsync(later, DerivedKind.Stream);
            using (await Plugins.StartedAsync("later"))
            {
                await Assert.ThrowsAsync<RetryLaterException>(() => GetAsync(shelf, file, DerivedKind.Stream));
                Plugins.GoOn("later");
            }

            Assert.Equal(MakingOutcome.Kept, await making);
        }

        /// <summary>None is made of contents that cannot be read, so none is on its way, though nothing is kept of that.</summary>
        [LinuxFact]
        public async Task GetAsync_AStreamOfContentsThatCannotBeRead_IsNone()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");
            string path = ObjectStore.GetPath(file.Sha256);
            File.Delete(path);

            // Opens, and fails each read at its start.
            File.CreateSymbolicLink(path, "/proc/self/mem");

            await Assert.ThrowsAsync<NotFoundException>(() => GetAsync(shelf, file, DerivedKind.Stream));
        }

        /// <summary>The quick lane's problems say nothing of the long lane's runs.</summary>
        [Fact]
        public async Task GetAsync_AStreamWhoseQuickRunsCouldNotRun_IsLater()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");
            Services.GetRequiredService<PluginProblems>().Report(TestMaker.Spec.DefaultId, PluginPart.Maker, PluginLane.Quick, "It could not run.");

            await Assert.ThrowsAsync<RetryLaterException>(() => GetAsync(shelf, file, DerivedKind.Stream));
        }

        [Theory]
        [InlineData("a", true)]
        [InlineData("throw", false)]
        public async Task GetAsync_AStreamTheBackgroundMadeOrFailedToMake_IsItOrNone(string contents, bool made)
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", contents);

            Assert.Equal(1, await MakeBacklogAsync(MakingGroup.Stream));

            if (made)
            {
                Assert.Equal(TestMaker.Made(DerivedKind.Stream, contents), await GetAsync(shelf, file, DerivedKind.Stream));
            }
            else
            {
                await Assert.ThrowsAsync<NotFoundException>(() => GetAsync(shelf, file, DerivedKind.Stream));
            }

            Assert.Equal([$"{contents}: Stream"], Plugins.Runs);
        }

        [Fact]
        public async Task GetAsync_AStreamThereWasNoRoomFor_IsNoRoomUntilThereIsAndThenLaterWithNothingMade()
        {
            Plugins.All = [Plugins.Plugin(kinds: [DerivedKind.Stream])];
            Shelf shelf = await CreateShelfAsync(quotaBytes: "a".Length);
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");
            await MakeBacklogAsync(MakingGroup.Stream);
            Assert.IsType<DerivedOutcome.NoRoom>(Store.Find(DerivedKind.Stream, file.Sha256)!.Outcome);

            await Assert.ThrowsAsync<QuotaExceededException>(() => GetAsync(shelf, file, DerivedKind.Stream));
            await ShelfService.UpdateAsync(shelf, shelf.Name, quotaBytes: null);

            // Unlike a thumbnail, which is made again as it is asked for, a stream is left to the background.
            await Assert.ThrowsAsync<RetryLaterException>(() => GetAsync(shelf, file, DerivedKind.Stream));
            Assert.Single(Plugins.Runs);
            Assert.Equal(1, await MakeBacklogAsync(MakingGroup.Stream));
            Assert.Equal(TestMaker.Made(DerivedKind.Stream, "a"), await GetAsync(shelf, file, DerivedKind.Stream));
        }

        [Fact]
        public async Task Problems_OfMakingStreams_AreNotClearedByThumbnailsBeingMade()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile full = await StoreTextAsync(shelf, "/full.txt", "full disk");
            await Assert.ThrowsAsync<PluginUnavailableException>(() => MakeAsync(full, DerivedKind.Stream));

            Assert.Equal(TestMaker.Made(DerivedKind.Thumbnail, "b"), await GetAsync(shelf, await StoreTextAsync(shelf, "/b.txt", "b")));

            Assert.Equal(
                "Its maker of derived files could not do its long runs, so it does none until it can: the disk is full.",
                Services.GetRequiredService<PluginProblems>().Of(TestMaker.Spec.DefaultId, PluginPart.Maker));
        }

        [Fact]
        public async Task MakeBacklogAsync_EachGroup_MakesItsOwnKindsAlone()
        {
            ShelfFile file = await StoreTextAsync(await CreateShelfAsync(), "/a.txt", "a");

            Assert.Equal((1, 0), (await MakeBacklogAsync(MakingGroup.Background), await MakeBacklogAsync(MakingGroup.Background)));
            AssertKept((DerivedKind.Thumbnail, file), (DerivedKind.Preview, file));

            Assert.Equal((1, 0), (await MakeBacklogAsync(MakingGroup.Stream), await MakeBacklogAsync(MakingGroup.Stream)));
            AssertKept((DerivedKind.Thumbnail, file), (DerivedKind.Preview, file), (DerivedKind.Stream, file));
        }

        [Fact]
        public async Task DerivedMaking_OfEachGroup_IsAJobOfItsOwnThatRunsThePluginsThatMakeIt()
        {
            Plugins.All = [Plugins.Plugin("thumbnails", kinds: [DerivedKind.Thumbnail, DerivedKind.Preview]), Plugins.Plugin("streams", kinds: [DerivedKind.Stream])];
            ShelfFile file = await StoreTextAsync(await CreateShelfAsync(), "/a.txt", "a");
            using DerivedMakingWorker streams = ActivatorUtilities.CreateInstance<DerivedMakingWorker>(Services, MakingGroup.Stream);

            await streams.StartAsync(CancellationToken.None);
            await TestDerivedPlugins.UntilAsync(() => Store.Find(DerivedKind.Stream, file.Sha256) != null);
            await streams.StopAsync(CancellationToken.None);

            AssertKept((DerivedKind.Stream, file));
            Assert.Equal("streams", Store.Find(DerivedKind.Stream, file.Sha256)!.Plugin);
        }

        [Fact]
        public void DerivedMaking_OfAGroupMadeOnlyWhenAsked_IsNone()
        {
            Assert.Throws<ArgumentException>(() => ActivatorUtilities.CreateInstance<DerivedMakingWorker>(Services, MakingGroup.Full));
        }

        [Fact]
        public async Task StoredContents_OfPluginsMakingOneGroupEach_HasEachMakeItsOwn()
        {
            Plugins.All = [Plugins.Plugin("thumbnails", kinds: [DerivedKind.Thumbnail, DerivedKind.Preview]), Plugins.Plugin("streams", kinds: [DerivedKind.Stream])];
            using StoredContentsWorker worker = ActivatorUtilities.CreateInstance<StoredContentsWorker>(Services);
            await worker.StartAsync(CancellationToken.None);

            ShelfFile file = await StoreTextAsync(await CreateShelfAsync(), "/a.txt", "a");
            await TestDerivedPlugins.UntilAsync(() => Store.Find(DerivedKind.Stream, file.Sha256) != null && Store.Find(DerivedKind.Preview, file.Sha256) != null);
            await worker.StopAsync(CancellationToken.None);

            Assert.Equal(["a: Stream", "a: Thumbnail, Preview"], Plugins.Runs.Order());
            Assert.Equal(("thumbnails", "streams"), (Store.Find(DerivedKind.Thumbnail, file.Sha256)!.Plugin, Store.Find(DerivedKind.Stream, file.Sha256)!.Plugin));
        }

        [Fact]
        public async Task StoredContents_AStreamBeingMade_HoldsUpNoThumbnailOfWhatIsStoredAfter()
        {
            using StoredContentsWorker worker = ActivatorUtilities.CreateInstance<StoredContentsWorker>(Services);
            await worker.StartAsync(CancellationToken.None);
            Shelf shelf = await CreateShelfAsync();
            ShelfFile first = await StoreTextAsync(shelf, "/first.txt", Plugins.Waiting("first", DerivedKind.Stream));

            using (await Plugins.StartedAsync("first"))
            {
                ShelfFile second = await StoreTextAsync(shelf, "/second.txt", "second");
                await TestDerivedPlugins.UntilAsync(() => Store.Find(DerivedKind.Preview, second.Sha256) != null);
                Assert.Null(Store.Find(DerivedKind.Stream, first.Sha256));
                Assert.Null(Store.Find(DerivedKind.Stream, second.Sha256));

                Plugins.GoOn("first");
                await TestDerivedPlugins.UntilAsync(() => Store.Find(DerivedKind.Stream, second.Sha256) != null);
            }

            await worker.StopAsync(CancellationToken.None);
        }
    }
}
