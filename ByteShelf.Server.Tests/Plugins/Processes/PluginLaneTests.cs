using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Derived.Services;
using ByteShelf.Server.Shared.Processes;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Tests.Derived;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.Plugins.Processes
{
    /// <summary>
    /// A plugin's runs that may take hours (<see cref="MakingGroup.Stream"/>), in the long lane: a process and a turn of
    /// their own, a day's time, and the lowest priority, beside those of a minute.
    /// </summary>
    public sealed class PluginLaneTests : DerivedTestBase
    {
        // Written out rather than taken from the code, so a change of them is noticed.
        private static readonly TimeSpan LongRunLimit = TimeSpan.FromHours(24);
        private const int LowestNice = 19;

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            Plugins.All = [Plugins.Plugin(kinds: [DerivedKind.Thumbnail, DerivedKind.Preview, DerivedKind.Stream])];
        }

        [UnixFact]
        public async Task RunAsync_AStreamThatHangs_GoesOnUntilADayHasPassedWhileTheQuickLaneMakesTheRest()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile hanging = await StoreTextAsync(shelf, "/hanging.txt", "hang");
            Task<MakingOutcome> making = MakeAsync(hanging, DerivedKind.Stream);
            await TestDerivedPlugins.UntilAsync(() => Plugins.Runs.Count == 1);
            await Clock.WaitForTimerAsync(LongRunLimit);

            Clock.Advance(LongRunLimit - TimeSpan.FromMilliseconds(1));

            // Its own turn and process: the hanging run holds up none of the plugin's others.
            ShelfFile other = await StoreTextAsync(shelf, "/b.txt", "b");
            Assert.Equal(TestMaker.Made(DerivedKind.Thumbnail, "b"), await GetAsync(shelf, other).WaitAsync(RealTimeLimit));
            Assert.False(making.IsCompleted);

            Clock.Advance(TimeSpan.FromMilliseconds(1));

            Assert.Equal(MakingOutcome.Kept, await making);
            Assert.IsType<DerivedOutcome.Failed>(Store.Find(DerivedKind.Stream, hanging.Sha256)!.Outcome);
            Assert.Equal(["hang: Stream", "b: Thumbnail, Preview"], Plugins.Runs);
        }

        [LinuxTheory]
        [InlineData(DerivedKind.Stream, true)]
        [InlineData(DerivedKind.Thumbnail, false)]
        public async Task RunAsync_InEachLane_IsAtItsPriorityWithEveryThreadAndProcessItStarts(DerivedKind kind, bool lowest)
        {
            // Else a run at the lowest could not be told from one at the tests' own.
            Assert.True(ProcStat.Of(Environment.ProcessId).Nice < LowestNice, "The tests run at the lowest priority already.");
            ShelfFile file = await StoreTextAsync(await CreateShelfAsync(), "/a.txt", Plugins.WithChild("a", "wait"));
            Task<MakingOutcome> making = MakeAsync(file, kind);
            int child = await Plugins.ChildOfAsync("a");
            IReadOnlyList<ProcStat> process = ProcStat.OfThreads(ProcStat.Of(child).Parent);

            // The runtime's own threads, then the plugin's, and the process it started.
            IReadOnlyList<ProcStat> threads = [.. process, .. ProcStat.OfThreads(child)];

            int expected = lowest ? LowestNice : ProcStat.Of(Environment.ProcessId).Nice;
            Assert.True(process.Count > 1, $"The plugin's process has {process.Count} thread, where the runtime starts several.");
            Assert.All(threads, thread => Assert.Equal(expected, thread.Nice));
            await StopAsync(file, making);
        }

        [LinuxFact]
        public async Task StopAsync_OfContentsAStreamIsBeingMadeOf_EndsTheLongLanesProcessAndWhatItStarted()
        {
            ShelfFile file = await StoreTextAsync(await CreateShelfAsync(), "/a.txt", Plugins.WithChild("a", "wait"));
            Task<MakingOutcome> making = MakeAsync(file, DerivedKind.Stream);
            int child = await Plugins.ChildOfAsync("a");
            int process = ProcStat.Of(child).Parent;

            await Processes.StopAsync(file.Sha256).WaitAsync(RealTimeLimit);

            Assert.Equal(MakingOutcome.Stopped, await making);
            Assert.False(TestDerivedPlugins.IsRunning(process));
            Assert.False(TestDerivedPlugins.IsRunning(child));
            Assert.Null(Store.Find(DerivedKind.Stream, file.Sha256));
        }

        [UnixFact]
        public async Task StopAsync_OfContentsAStreamWasMadeOfBefore_LeavesTheStreamBeingMadeOfOthersAlone()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile first = await StoreTextAsync(shelf, "/first.txt", "first");
            Assert.Equal(MakingOutcome.Kept, await MakeAsync(first, DerivedKind.Stream));
            string waiting = Plugins.Waiting("second", DerivedKind.Stream);
            ShelfFile second = await StoreTextAsync(shelf, "/second.txt", waiting);
            Task<MakingOutcome> making = MakeAsync(second, DerivedKind.Stream);

            using (await Plugins.StartedAsync("second"))
            {
                await Processes.StopAsync(first.Sha256).WaitAsync(RealTimeLimit);
                Plugins.GoOn("second");
            }

            // Once: the run was not ended to be done anew in a process that never read the first.
            Assert.Equal(MakingOutcome.Kept, await making);
            Assert.Equal(["first: Stream", $"{waiting}: Stream"], Plugins.Runs);
        }

        [LinuxFact]
        public void TakeLowestPriority_InAProcessThatLeadsNoGroup_IsRefusedRatherThanLoweringItsParentsGroup()
        {
            int before = ProcStat.Of(Environment.ProcessId).Nice;

            // The tests' process leads no group: only a plugin's own process calls ProcessGroup.Lead, at its start.
            Assert.Throws<InvalidOperationException>(ProcessGroup.TakeLowestPriority);

            Assert.Equal(before, ProcStat.Of(Environment.ProcessId).Nice);
        }

        /// <summary>Ends the run, whatever came of it: told to go on just as its contents are removed, it may be either.</summary>
        private async Task StopAsync(ShelfFile file, Task<MakingOutcome> making)
        {
            Plugins.GoOn("a");
            await Processes.StopAsync(file.Sha256).WaitAsync(RealTimeLimit);
            await making;
        }
    }
}
