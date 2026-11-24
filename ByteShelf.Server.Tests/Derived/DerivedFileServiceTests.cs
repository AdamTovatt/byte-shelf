using System.Diagnostics;
using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Derived.Services;
using ByteShelf.Server.Plugins.Processes;
using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ByteShelf.Server.Tests.Derived
{
    /// <summary>Derived files asked for: made in a child process when they are missing, once, and kept.</summary>
    public sealed class DerivedFileServiceTests : DerivedTestBase
    {
        // Written out rather than taken from the code, so a change of one is noticed.
        private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan RunLimit = TimeSpan.FromSeconds(60);

        private readonly RecordingLoggerFactory _log = new RecordingLoggerFactory();

        protected override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);
            services.AddSingleton<ILoggerFactory>(_log);
        }

        [Fact]
        public async Task GetAsync_Missing_IsMadeWithWhatIsMadeAlongAndKept()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "hello");

            Assert.Equal("Thumbnail of hello by test-maker 1", await GetAsync(shelf, file));
            Assert.Equal("Preview of hello by test-maker 1", await GetAsync(shelf, file, DerivedKind.Preview));
            Assert.Equal("Thumbnail of hello by test-maker 1", await GetAsync(shelf, file));

            Assert.Equal(["hello: Thumbnail, Preview"], Plugins.Runs);
            AssertKept((DerivedKind.Thumbnail, file), (DerivedKind.Preview, file));
        }

        [Theory]
        [InlineData(DerivedKind.Full)]
        [InlineData(DerivedKind.Agent)]
        public async Task GetAsync_AKindMadeOnlyWhenAskedFor_IsMadeAlone(DerivedKind kind)
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "hello");

            Assert.Equal(TestMaker.Made(kind, "hello"), await GetAsync(shelf, file, kind));

            Assert.Equal([$"hello: {kind}"], Plugins.Runs);
        }

        [Fact]
        public async Task GetAsync_ForSeveralAtOnce_IsMadeOnce()
        {
            Shelf shelf = await CreateShelfAsync();
            string contents = Plugins.Waiting("a");
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", contents);
            ShelfFile copy = await StoreTextAsync(shelf, "/copy.txt", contents);

            Task<string>[] asked = [GetAsync(shelf, file), GetAsync(shelf, copy), GetAsync(shelf, file, DerivedKind.Preview)];
            using (await Plugins.StartedAsync("a"))
            {
                Plugins.GoOn("a");
            }

            string[] made = await Task.WhenAll(asked);
            Assert.Equal([TestMaker.Made(DerivedKind.Thumbnail, contents), TestMaker.Made(DerivedKind.Thumbnail, contents), TestMaker.Made(DerivedKind.Preview, contents)], made);
            Assert.Single(Plugins.Runs);
        }

        [Fact]
        public async Task GetAsync_NotMadeInTime_IsToldToComeBackWhileItIsMade()
        {
            Shelf shelf = await CreateShelfAsync();
            string contents = Plugins.Waiting("a");
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", contents);
            Task<string> asked = GetAsync(shelf, file);
            using (await Plugins.StartedAsync("a"))
            {
                await Clock.WaitForTimerAsync(WaitLimit);
                Clock.Advance(WaitLimit - TimeSpan.FromMilliseconds(1));
                Assert.False(asked.IsCompleted);
                Clock.Advance(TimeSpan.FromMilliseconds(1));

                RetryLaterException later = await Assert.ThrowsAsync<RetryLaterException>(() => asked);
                Assert.True(later.RetryAfter > TimeSpan.Zero);
                Plugins.GoOn("a");
            }

            Assert.Equal(TestMaker.Made(DerivedKind.Thumbnail, contents), await GetAsync(shelf, file));
            Assert.Single(Plugins.Runs);
        }

        [Theory]
        [InlineData("throw secret", "System.IO.InvalidDataException")]
        [InlineData("crash secret", "its process ended with exit code 134")]
        public async Task GetAsync_WhatFails_IsNotTriedAgainUntilTheVersionChangesNorQuotedInTheLog(string contents, string logged)
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", contents);

            await Assert.ThrowsAsync<NotFoundException>(() => GetAsync(shelf, file));
            await Assert.ThrowsAsync<NotFoundException>(() => GetAsync(shelf, file, DerivedKind.Preview));
            Assert.Single(Plugins.Runs);
            Assert.Contains(_log.Messages, message => message.Contains(logged, StringComparison.Ordinal));
            Assert.DoesNotContain(_log.Messages, message => message.Contains("secret", StringComparison.Ordinal));

            Plugins.All = [Plugins.Plugin(version: 2)];
            await Assert.ThrowsAsync<NotFoundException>(() => GetAsync(shelf, file));
            Assert.Equal(2, Plugins.Runs.Count);
        }

        [Fact]
        public async Task GetAsync_AKindTheMakerLeavesOut_HasFailedAlone()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "without preview");

            Assert.Equal("Thumbnail of without preview by test-maker 1", await GetAsync(shelf, file));
            await Assert.ThrowsAsync<NotFoundException>(() => GetAsync(shelf, file, DerivedKind.Preview));
            Assert.Single(Plugins.Runs);
        }

        [Fact]
        public async Task GetAsync_ARunOverItsTime_IsKilledAndHasFailed()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", Plugins.Waiting("a"));
            Task<MakingOutcome> making = MakeAsync(file);
            using Process process = await Plugins.StartedAsync("a");

            await Clock.WaitForTimerAsync(RunLimit);
            Clock.Advance(RunLimit - TimeSpan.FromMilliseconds(1));
            Assert.False(process.HasExited);
            Clock.Advance(TimeSpan.FromMilliseconds(1));

            Assert.Equal(MakingOutcome.Kept, await making);
            Assert.True(process.HasExited);
            await Assert.ThrowsAsync<NotFoundException>(() => GetAsync(shelf, file));
            Assert.Single(Plugins.Runs);
        }

        [Fact]
        public async Task GetAsync_AMakerThatHangs_IsKilled()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "hang");
            Task<MakingOutcome> making = MakeAsync(file);
            await TestDerivedPlugins.UntilAsync(() => Plugins.Runs.Count == 1);

            await Clock.WaitForTimerAsync(RunLimit);
            Clock.Advance(RunLimit);

            Assert.Equal(MakingOutcome.Kept, await making);
            Assert.Equal(new DerivedEntry(TestMaker.Spec.DefaultId, 1, new DerivedOutcome.Failed()), Store.Find(DerivedKind.Thumbnail, file.Sha256));
        }

        [Fact]
        public async Task GetAsync_AMakerThatWritesMoreThanAPipeHolds_IsNotHeldUp()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "noise");

            Assert.Equal(TestMaker.Made(DerivedKind.Thumbnail, "noise"), await GetAsync(shelf, file));
        }

        [Fact]
        public async Task GetAsync_AMakerThatLeavesTemporaryFiles_LeavesNothingBehind()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "temporary");

            await GetAsync(shelf, file);

            string temporary = Plugins.Runs.Single(line => line.StartsWith('/') || line.Contains(":\\", StringComparison.Ordinal));
            Assert.False(File.Exists(temporary), temporary);
            AssertKept((DerivedKind.Thumbnail, file), (DerivedKind.Preview, file));
        }

        [Fact]
        public async Task GetAsync_AKindLargerThanItMayBe_HasFailedAlone()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "large thumbnail");

            await Assert.ThrowsAsync<NotFoundException>(() => GetAsync(shelf, file));

            Assert.Equal(TestMaker.Made(DerivedKind.Preview, "large thumbnail"), await GetAsync(shelf, file, DerivedKind.Preview));
            Assert.Single(Plugins.Runs);
        }

        [Theory]
        [InlineData("full disk")]
        [InlineData("in the way")]
        public async Task MakeAsync_WhatCouldNotBeWritten_IsNotTheContentsFailure(string contents)
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", contents);

            await Assert.ThrowsAsync<PluginUnavailableException>(() => GetAsync(shelf, file));

            Assert.Empty(AllDerivedFiles());
        }

        [LinuxFact]
        public async Task MakeAsync_AFullDiskTheMakerLetsPass_IsNotTheContentsFailure()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "filled disk");

            await Assert.ThrowsAsync<PluginUnavailableException>(() => GetAsync(shelf, file));

            Assert.Empty(AllDerivedFiles());
        }

        [UnixFact]
        public async Task MakeAsync_AContentsThatCannotBeOpened_RecordsNothing()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "hello");
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(ObjectStore.GetPath(file.Sha256), UnixFileMode.None);
            }

            Assert.Equal(MakingOutcome.NothingToMake, await MakeAsync(file));

            Assert.Empty(AllDerivedFiles());
        }

        [Fact]
        public async Task MakeAsync_Full_TrimsTheLeastRecentlyUsedWithoutWaitingForAnyInUse()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile[] old = [await StoreTextAsync(shelf, "/a.txt", "a"), await StoreTextAsync(shelf, "/b.txt", "b")];
            foreach (ShelfFile file in old)
            {
                await PutLargeFullAsync(file);
            }

            ShelfFile now = await StoreTextAsync(shelf, "/c.txt", "c");
            Clock.Advance(TimeSpan.FromMinutes(1));

            // As a removal of those contents holds it, which may wait for everything made from them to end.
            using (await Locks.LockAsync(old[0].Sha256))
            {
                Assert.Equal(TestMaker.Made(DerivedKind.Full, "c"), await GetAsync(shelf, now, DerivedKind.Full).WaitAsync(RealTimeLimit));
            }

            AssertKept((DerivedKind.Full, old[0]), (DerivedKind.Full, now));
        }

        [Fact]
        public async Task GetAsync_MadeAtAnotherVersion_IsMadeAgain()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "hello");
            await GetAsync(shelf, file);

            Plugins.All = [Plugins.Plugin(version: 2)];

            Assert.Equal("Thumbnail of hello by test-maker 2", await GetAsync(shelf, file));
            Assert.Equal(["hello: Thumbnail, Preview", "hello: Thumbnail, Preview"], Plugins.Runs);
        }

        [Fact]
        public async Task GetAsync_MadeByAnotherPlugin_IsMadeAgain()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "hello");
            await GetAsync(shelf, file);

            Plugins.All = [Plugins.Plugin("other")];

            Assert.Equal("Thumbnail of hello by other 1", await GetAsync(shelf, file));
        }

        [Fact]
        public async Task GetAsync_OnceTheFolderIsEmptied_IsMadeAgain()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "hello");
            await GetAsync(shelf, file);

            Directory.Delete(TestRoot.Root.DerivedPath, recursive: true);

            Assert.Equal("Thumbnail of hello by test-maker 1", await GetAsync(shelf, file));
        }

        [Fact]
        public async Task GetAsync_OneThatIsGoneFromBesideItsEntry_IsMadeAgainAlone()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "hello");
            await GetAsync(shelf, file);

            File.Delete(Store.GetPath(DerivedKind.Thumbnail, file.Sha256));

            Assert.Equal("Thumbnail of hello by test-maker 1", await GetAsync(shelf, file));
            Assert.Equal(["hello: Thumbnail, Preview", "hello: Thumbnail"], Plugins.Runs);
        }

        [Fact]
        public async Task GetAsync_AFileWhoseOwnTypeNothingTakes_HasNoneThoughAnotherWithItsContentsHas()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile text = await StoreAsync(shelf, "/a.txt", "1"u8.ToArray(), "text/plain");
            ShelfFile other = await StoreAsync(shelf, "/a.bin", "1"u8.ToArray(), "application/octet-stream");
            await GetAsync(shelf, text);

            await Assert.ThrowsAsync<NotFoundException>(() => GetAsync(shelf, other));
        }

        [Fact]
        public async Task GetAsync_WhatIsStoppedAsItIsWaitedFor_IsToldToComeBack()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", Plugins.Waiting("a"));
            Task<string> asked = GetAsync(shelf, file);
            using (await Plugins.StartedAsync("a"))
            {
                await Processes.StopAsync(file.Sha256).WaitAsync(RealTimeLimit);
            }

            await Assert.ThrowsAsync<RetryLaterException>(() => asked);
        }

        [Fact]
        public async Task GetAsync_ARequestThatGoesAway_EndsWhileItIsMade()
        {
            Shelf shelf = await CreateShelfAsync();
            string contents = Plugins.Waiting("a");
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", contents);
            using CancellationTokenSource request = new CancellationTokenSource();
            Task<DerivedFile> asked = DerivedFiles.GetAsync(shelf, EntryReference.ForId(file.Id), DerivedKind.Thumbnail, request.Token);
            using (await Plugins.StartedAsync("a"))
            {
                await request.CancelAsync();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => asked.WaitAsync(RealTimeLimit));
                Plugins.GoOn("a");
            }

            Assert.Equal(TestMaker.Made(DerivedKind.Thumbnail, contents), await GetAsync(shelf, file));
            Assert.Single(Plugins.Runs);
        }

        [Fact]
        public async Task GetAsync_OneContentsHeldAsTypesOfTwoPlugins_IsMadeByOneOfThem()
        {
            Plugins.All = [Plugins.Plugin("b", 1, null, "text/plain"), Plugins.Plugin("a", 1, null, "application/json")];
            Shelf shelf = await CreateShelfAsync();
            ShelfFile text = await StoreAsync(shelf, "/a.txt", "1"u8.ToArray(), "text/plain");
            ShelfFile json = await StoreAsync(shelf, "/a.json", "1"u8.ToArray(), "application/json");

            Assert.Equal("Thumbnail of 1 by a 1", await GetAsync(shelf, text));
            Assert.Equal("Thumbnail of 1 by a 1", await GetAsync(shelf, json));
            Assert.Equal("Thumbnail of 1 by a 1", await GetAsync(shelf, text));

            Assert.Single(Plugins.Runs);
        }

        [Fact]
        public async Task GetAsync_WhatNothingMakes_IsNotFound()
        {
            Plugins.All = [Plugins.Plugin(kinds: [DerivedKind.Thumbnail])];
            Shelf shelf = await CreateShelfAsync();
            ShelfFile text = await StoreTextAsync(shelf, "/a.txt", "hello");
            ShelfFile other = await StoreAsync(shelf, "/a.bin", [1], "application/octet-stream");

            await Assert.ThrowsAsync<NotFoundException>(() => GetAsync(shelf, text, DerivedKind.Preview));
            await Assert.ThrowsAsync<NotFoundException>(() => GetAsync(shelf, other));
            Assert.Empty(Plugins.Runs);
        }

        [Fact]
        public async Task GetAsync_AnEncryptedFile_HasNoneEvenWhenAPlainOneHasTheSameBytes()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile plain = await StoreAsync(shelf, "/plain.txt", AgeBytes, "text/plain");
            ShelfFile encrypted = await StoreAsync(shelf, "/encrypted.txt", AgeBytes, "text/plain", encrypted: true);
            await GetAsync(shelf, plain);

            await Assert.ThrowsAsync<NotFoundException>(() => GetAsync(shelf, encrypted));
            Assert.Single(Plugins.Runs);
        }

        [Fact]
        public async Task GetDeletedAsync_AFileInTheTrash_HasItMade()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "hello");
            TrashEntry deleted = await FileService.DeleteFileAsync(shelf, EntryReference.ForId(file.Id));

            await using DerivedFile derived = await DerivedFiles.GetDeletedAsync(shelf, deleted.DeletionId, file.Id, DerivedKind.Thumbnail, CancellationToken.None);

            Assert.Equal(file.Sha256, derived.Sha256);
        }

        [Fact]
        public async Task GetDeletedAsync_ByAnotherDeletion_IsNotFound()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "hello");
            ShelfFile other = await StoreTextAsync(shelf, "/b.txt", "other");
            await FileService.DeleteFileAsync(shelf, EntryReference.ForId(file.Id));
            TrashEntry another = await FileService.DeleteFileAsync(shelf, EntryReference.ForId(other.Id));

            foreach (Guid deletionId in new[] { another.DeletionId, Guid.NewGuid() })
            {
                await Assert.ThrowsAsync<NotFoundException>(() => DerivedFiles.GetDeletedAsync(shelf, deletionId, file.Id, DerivedKind.Thumbnail, CancellationToken.None));
            }
        }

        [Fact]
        public async Task MakeAsync_OnePluginsRuns_AreOneAtATimeAndThoseAskedForFirst()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile first = await StoreTextAsync(shelf, "/first.txt", Plugins.Waiting("first"));
            ShelfFile second = await StoreTextAsync(shelf, "/second.txt", "second");
            ShelfFile asked = await StoreTextAsync(shelf, "/asked.txt", "asked");
            ShelfFile raised = await StoreTextAsync(shelf, "/raised.txt", "raised");
            Task<MakingOutcome>[] background =
            [
                MakeAsync(first),
                MakeAsync(second),
                MakeAsync(raised),
            ];
            using (await Plugins.StartedAsync("first"))
            {
                Task<MakingOutcome> now = MakeAsync(asked, DerivedKind.Agent, PluginPriority.Asked);
                Task<MakingOutcome> alsoNow = MakeAsync(raised, DerivedKind.Preview, PluginPriority.Asked);
                Assert.Single(Plugins.Runs);
                Plugins.GoOn("first");
                await Task.WhenAll([.. background, now, alsoNow]);
            }

            Assert.Equal(["raised: Thumbnail, Preview", "asked: Agent", "second: Thumbnail, Preview"], Plugins.Runs.Skip(1));
        }

        [Fact]
        public async Task MakeAsync_TwoPlugins_DoNotWaitForEachOther()
        {
            Plugins.All = [Plugins.Plugin("a", 1, null, "text/plain"), Plugins.Plugin("b", 1, null, "text/csv")];
            Shelf shelf = await CreateShelfAsync();
            ShelfFile slow = await StoreTextAsync(shelf, "/a.txt", Plugins.Waiting("a"));
            ShelfFile quick = await StoreAsync(shelf, "/b.csv", "b"u8.ToArray(), "text/csv");
            Task<MakingOutcome> making = MakeAsync(slow);
            using (await Plugins.StartedAsync("a"))
            {
                Assert.Equal("Thumbnail of b by b 1", await GetAsync(shelf, quick));
                Plugins.GoOn("a");
            }

            await making;
        }

        [Fact]
        public async Task MakeAsync_AContentsGoneMeanwhile_RecordsNothing()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "hello");
            ObjectStore.Delete(file.Sha256);

            await Assert.ThrowsAsync<NotFoundException>(() => GetAsync(shelf, file));

            Assert.Empty(AllDerivedFiles());
        }

        [Theory]
        [InlineData("")]
        [InlineData("other version")]
        public async Task MakeAsync_APluginTheChildCannotLoad_FailsWithoutBlamingTheContents(string location)
        {
            DerivedFilePlugin installed = Plugins.Plugin();
            Plugins.All = [installed with { Target = location.Length == 0 ? new PluginTarget(installed.Id, location) : Plugins.Plugin(version: 2).Target }];
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "hello");

            await Assert.ThrowsAsync<PluginUnavailableException>(() => GetAsync(shelf, file));

            Assert.Empty(AllDerivedFiles());
            Assert.Empty(Plugins.Runs);
        }
    }
}
