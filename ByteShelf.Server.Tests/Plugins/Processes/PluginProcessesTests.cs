using System.Diagnostics;
using System.Globalization;
using System.Text;
using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Derived.Services;
using ByteShelf.Server.Plugins.Processes;
using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Tests.Derived;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ByteShelf.Server.Tests.Plugins.Processes
{
    /// <summary>The child processes plugins run in, through what makes derived files: kept, replaced and ended.</summary>
    public sealed class PluginProcessesTests : DerivedTestBase
    {
        // Written out rather than taken from the code, so a change of it is noticed.
        private static readonly TimeSpan IdleLimit = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan RunLimit = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan KillLimit = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan KillPatience = TimeSpan.FromSeconds(20);

        private PluginExecutable _program = TestDerivedPlugins.Executable;

        protected override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);
            services.Replace(ServiceDescriptor.Singleton(_ => _program));
        }

        [Fact]
        public async Task RunAsync_OneAfterTheOther_AreInOneProcess()
        {
            Shelf shelf = await CreateShelfAsync();
            using Process first = await RunAsync(await StoreTextAsync(shelf, "/a.txt", Plugins.Waiting("a")), "a");
            using Process second = await RunAsync(await StoreTextAsync(shelf, "/b.txt", Plugins.Waiting("b")), "b");

            Assert.Equal(first.Id, second.Id);
        }

        [Fact]
        public async Task RunAsync_NothingForAMinute_EndsTheProcess()
        {
            ShelfFile file = await StoreTextAsync(await CreateShelfAsync(), "/a.txt", Plugins.Waiting("a"));
            using Process process = await RunAsync(file, "a");
            await Clock.WaitForTimerAsync(IdleLimit);

            Clock.Advance(IdleLimit - TimeSpan.FromMilliseconds(1));
            Assert.False(process.HasExited);
            Clock.Advance(TimeSpan.FromMilliseconds(1));

            await TestDerivedPlugins.UntilAsync(() => process.HasExited && WorkFolder.Find(TestRoot.Root).Count == 0);
        }

        [UnixTheory]
        [InlineData("crash")]
        [InlineData("crash natively")]
        public async Task RunAsync_ACrash_IsTheContentsFailureAndTheNextHasANewProcess(string contents)
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile crashing = await StoreTextAsync(shelf, "/a.txt", contents);

            await Assert.ThrowsAsync<NotFoundException>(() => GetAsync(shelf, crashing));
            Assert.Equal("Thumbnail of b by test-maker 1", await GetAsync(shelf, await StoreTextAsync(shelf, "/b.txt", "b")));

            await Assert.ThrowsAsync<NotFoundException>(() => GetAsync(shelf, crashing));
            Assert.Equal([$"{contents}: Thumbnail, Preview", "b: Thumbnail, Preview"], Plugins.Runs);
        }

        [Theory]
        [InlineData("throw", null)]
        [InlineData("hoard", TestMaker.MediaType)]
        public async Task RunAsync_OneThatMayLeaveTheNextWorseOff_EndsTheProcess(string contents, string? made)
        {
            ShelfFile file = await StoreTextAsync(await CreateShelfAsync(), "/a.txt", contents);

            Assert.Equal(MakingOutcome.Kept, await MakeAsync(file));

            Assert.Equal(made, Store.Find(DerivedKind.Thumbnail, file.Sha256)!.Made?.MediaType);
            Assert.Empty(WorkFolder.Find(TestRoot.Root));
        }

        [LinuxTheory]
        [InlineData("eat managed memory")]
        [InlineData("eat native memory")]
        public async Task RunAsync_OneThatTakesAllTheMemoryItCan_IsTheContentsFailureAlone(string contents)
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile eating = await StoreTextAsync(shelf, "/a.txt", contents);

            // The clock stands, so it is no time limit that ends it.
            Assert.Equal(MakingOutcome.Kept, await MakeAsync(eating));

            Assert.Null(Store.Find(DerivedKind.Thumbnail, eating.Sha256)!.Made);
            Assert.Equal("Thumbnail of b by test-maker 1", await GetAsync(shelf, await StoreTextAsync(shelf, "/b.txt", "b")));
        }

        [UnixFact]
        public async Task RunAsync_OneThatHangsInNativeCode_IsEndedWithItsTime()
        {
            ShelfFile file = await StoreTextAsync(await CreateShelfAsync(), "/a.txt", "hang natively");
            Task<MakingOutcome> making = MakeAsync(file);
            await TestDerivedPlugins.UntilAsync(() => Plugins.Runs.Count == 1);
            await Clock.WaitForTimerAsync(RunLimit);

            Clock.Advance(RunLimit);

            Assert.Equal(MakingOutcome.Kept, await making);
            Assert.Null(Store.Find(DerivedKind.Thumbnail, file.Sha256)!.Made);
        }

        [UnixFact]
        public async Task RunAsync_OneThatPrintsAndReadsTheStandardStreamsNatively_IsAnswered()
        {
            Shelf shelf = await CreateShelfAsync();

            Assert.Equal("Thumbnail of print natively by test-maker 1", await GetAsync(shelf, await StoreTextAsync(shelf, "/a.txt", "print natively")));
        }

        [Fact]
        public async Task RunAsync_AProcessThatDiedAsItHadNothingToDo_IsReplaced()
        {
            Shelf shelf = await CreateShelfAsync();
            using (Process process = await RunAsync(await StoreTextAsync(shelf, "/a.txt", Plugins.Waiting("a")), "a"))
            {
                process.Kill();
                await process.WaitForExitAsync();
            }

            Assert.Equal("Thumbnail of b by test-maker 1", await GetAsync(shelf, await StoreTextAsync(shelf, "/b.txt", "b")));
        }

        [Fact]
        public async Task StopAsync_OfContentsItReadBefore_EndsTheProcessAndDoesAnewWhatItWasDoing()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile before = await StoreTextAsync(shelf, "/before.txt", "before");
            string contents = Plugins.Waiting("now");
            ShelfFile now = await StoreTextAsync(shelf, "/now.txt", contents);
            await GetAsync(shelf, before);
            Task<MakingOutcome> making = MakeAsync(now);
            using Process process = await Plugins.StartedAsync("now");

            await EncryptInPlaceAsync(shelf, before);

            Assert.True(process.HasExited);
            Plugins.GoOn("now");
            Assert.Equal(MakingOutcome.Kept, await making);
            Assert.Equal(TestMaker.Made(DerivedKind.Thumbnail, contents), await GetAsync(shelf, now));
            Assert.Equal(2, Plugins.Runs.Count(run => run.StartsWith(contents, StringComparison.Ordinal)));
        }

        [Fact]
        public async Task StopAsync_OfContentsItHasNotRead_LeavesTheProcess()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile other = await StoreTextAsync(shelf, "/other.txt", "other");
            using Process process = await RunAsync(await StoreTextAsync(shelf, "/a.txt", Plugins.Waiting("a")), "a");

            await EncryptInPlaceAsync(shelf, other);

            Assert.False(process.HasExited);
        }

        [Fact]
        public async Task StopAsync_AsWhatItMadeOfOtherContentsIsTaken_WaitsUntilItHasBeen()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile before = await StoreTextAsync(shelf, "/before.txt", "before");
            ShelfFile now = await StoreTextAsync(shelf, "/now.txt", Plugins.Waiting("now"));
            await GetAsync(shelf, before);
            Task<MakingOutcome> making = MakeAsync(now);
            Task stopping;
            using (await Plugins.StartedAsync("now"))
            using (await Locks.LockAsync(now.Sha256))
            {
                TaskCompletionSource takes = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Locks.Locks.WaitingStarted = _ => takes.TrySetResult();
                Plugins.GoOn("now");
                await takes.Task.WaitAsync(RealTimeLimit);
                stopping = Processes.StopAsync(before.Sha256);
                await Task.WhenAny(stopping, Task.Delay(TimeSpan.FromSeconds(2)));
                Assert.False(stopping.IsCompleted);
            }

            await stopping.WaitAsync(RealTimeLimit);
            await making;
            Assert.NotNull(Store.Find(DerivedKind.Thumbnail, now.Sha256)!.Made);
        }

        [UnixFact]
        public async Task RunAsync_AProcessThatEndsBeforeItIsLoaded_IsNotTheContentsFailure()
        {
            _program = ShellProgram.Of("kill -9 $$");
            ShelfFile file = await StoreTextAsync(await CreateShelfAsync(), "/a.txt", "hello");

            await Assert.ThrowsAsync<PluginUnavailableException>(() => MakeAsync(file));

            Assert.Empty(AllDerivedFiles());
        }

        [UnixFact]
        public async Task RunAsync_AProcessThatEndsBeforeItBegins_IsNotTheContentsFailure()
        {
            _program = ShellProgram.Of($"{ShellProgram.Say(FrameType.Ready)}; {ShellProgram.TakeRequest}; kill -9 $$");
            ShelfFile file = await StoreTextAsync(await CreateShelfAsync(), "/a.txt", "hello");

            await Assert.ThrowsAsync<PluginUnavailableException>(() => MakeAsync(file));

            Assert.Empty(AllDerivedFiles());
        }

        [UnixFact]
        public async Task RunAsync_AProcessThatDoesNotBeginInTime_IsNotTheContentsFailure()
        {
            _program = ShellProgram.Of($"{ShellProgram.Say(FrameType.Ready)}; {ShellProgram.TakeRequest}; echo > loaded; exec sleep 1000");
            ShelfFile file = await StoreTextAsync(await CreateShelfAsync(), "/a.txt", "hello");
            Task<MakingOutcome> making = MakeAsync(file);
            await SaysAsync("loaded");
            await Clock.WaitForTimerAsync(RunLimit);

            Clock.Advance(RunLimit);

            await Assert.ThrowsAsync<PluginUnavailableException>(() => making);
            Assert.Empty(AllDerivedFiles());
        }

        [UnixTheory]
        [InlineData("a frame of no kind", "printf '\\143\\000\\000\\000\\000'")]
        [InlineData("a frame longer than any", "printf '\\006\\377\\377\\377\\177'")]
        [InlineData("a read of nothing", "printf '\\005\\001\\000\\000\\000\\000'")]
        [InlineData("an answer that is no json", "printf '\\006\\001\\000\\000\\000{'")]
        [InlineData("its end", "kill -9 $$")]
        public async Task RunAsync_AProcessThatBeganAndThenSaysWhatItMayNot_IsTheContentsFailure(string what, string script)
        {
            _program = ShellProgram.Of($"{ShellProgram.Say(FrameType.Ready)}; {ShellProgram.TakeRequest}; {ShellProgram.Say(FrameType.Started)}; {script}; sleep 1000");
            ShelfFile file = await StoreTextAsync(await CreateShelfAsync(), "/a.txt", what);

            Assert.Equal(MakingOutcome.Kept, await MakeAsync(file));

            Assert.Null(Store.Find(DerivedKind.Thumbnail, file.Sha256)!.Made);
        }

        [UnixFact]
        public async Task RunAsync_AFileThatIsNotThere_HasFailedAlone()
        {
            _program = Answering("""[{"kind":"thumbnail","mediaType":"image/webp"},{"kind":"preview","mediaType":"image/webp"}]""", "echo made > preview");
            ShelfFile file = await StoreTextAsync(await CreateShelfAsync(), "/a.txt", "hello");

            Assert.Equal(MakingOutcome.Kept, await MakeAsync(file));

            Assert.Null(Store.Find(DerivedKind.Thumbnail, file.Sha256)!.Made);
            Assert.NotNull(Store.Find(DerivedKind.Preview, file.Sha256)!.Made);
        }

        [UnixFact]
        public async Task RunAsync_WhatIsNotAMediaType_HasFailedAlone()
        {
            _program = Answering("""[{"kind":"thumbnail","mediaType":"not a type"},{"kind":"preview","mediaType":"image/webp"}]""", "echo made > preview; echo made > thumbnail");
            ShelfFile file = await StoreTextAsync(await CreateShelfAsync(), "/a.txt", "hello");

            Assert.Equal(MakingOutcome.Kept, await MakeAsync(file));

            Assert.Null(Store.Find(DerivedKind.Thumbnail, file.Sha256)!.Made);
            Assert.NotNull(Store.Find(DerivedKind.Preview, file.Sha256)!.Made);
        }

        [UnixFact]
        public async Task RunAsync_OneThatCrashes_TakesWhatItStartedWithIt()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", Plugins.WithChild("a", "crash"));

            await KillsAsync(Assert.ThrowsAsync<NotFoundException>(() => GetAsync(shelf, file)));

            Assert.False(TestDerivedPlugins.IsRunning(await Plugins.ChildOfAsync("a")));
        }

        [UnixFact]
        public async Task StopAsync_TakesWhatThePluginStartedWithIt()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", Plugins.WithChild("a", "wait"));
            Task<MakingOutcome> making = MakeAsync(file);
            int child = await Plugins.ChildOfAsync("a");
            Assert.True(TestDerivedPlugins.IsRunning(child));

            await KillsAsync(Processes.StopAsync(file.Sha256));

            Assert.Equal(MakingOutcome.Stopped, await making);
            Assert.False(TestDerivedPlugins.IsRunning(child));
        }

        [UnixFact]
        public async Task RunAsync_OneSentSignalsToStop_GoesOnAsTheServerEndsIt()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", Plugins.Waiting("a"));
            Task<MakingOutcome> making = MakeAsync(file);
            using (Process process = await Plugins.StartedAsync("a"))
            {
                foreach (string signal in new[] { "-TERM", "-INT", "-HUP", "-QUIT" })
                {
                    using Process sent = Process.Start("kill", [signal, process.Id.ToString(CultureInfo.InvariantCulture)]);
                    await sent.WaitForExitAsync();
                }

                Plugins.GoOn("a");
            }

            await making;
            Assert.NotNull(Store.Find(DerivedKind.Thumbnail, file.Sha256)!.Made);
        }

        [Fact]
        public async Task StopAllAsync_KillsWhatIsRunningAndRecordsNothing()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", Plugins.Waiting("a"));
            Task<MakingOutcome> making = MakeAsync(file);
            using Process process = await Plugins.StartedAsync("a");

            await Processes.StopAllAsync().WaitAsync(RealTimeLimit);

            Assert.Equal(MakingOutcome.Stopped, await making);
            Assert.True(process.HasExited);
            Assert.Empty(AllDerivedFiles());
            Assert.Equal(MakingOutcome.Stopped, await MakeAsync(file));
        }

        [UnixFact]
        public async Task RunAsync_AProcessThatDoesNotLoadInTime_IsNotTheContentsFailure()
        {
            _program = ShellProgram.Of("exec sleep 1000");
            ShelfFile file = await StoreTextAsync(await CreateShelfAsync(), "/a.txt", "hello");
            Task<MakingOutcome> making = MakeAsync(file);
            await Clock.WaitForTimerAsync(RunLimit);

            Clock.Advance(RunLimit);

            await Assert.ThrowsAsync<PluginUnavailableException>(() => making);
            Assert.Empty(AllDerivedFiles());
        }

        [UnixFact]
        public async Task RunAsync_AProcessThatSaysWhatItMayNotBeforeItIsLoaded_IsNotTheContentsFailure()
        {
            _program = ShellProgram.Of("printf '\\003\\377\\377\\377\\177'; exec sleep 1000");
            ShelfFile file = await StoreTextAsync(await CreateShelfAsync(), "/a.txt", "hello");

            await Assert.ThrowsAsync<PluginUnavailableException>(() => MakeAsync(file));

            Assert.Empty(AllDerivedFiles());
        }

        [Fact]
        public async Task RunAsync_AProcessThatCannotBeStarted_LeavesNoFolder()
        {
            _program = new PluginExecutable(Path.Combine(TestRoot.Root.DerivedPath, "nowhere"), []);
            ShelfFile file = await StoreTextAsync(await CreateShelfAsync(), "/a.txt", "hello");

            await Assert.ThrowsAsync<PluginUnavailableException>(() => MakeAsync(file));

            Assert.Empty(WorkFolder.Find(TestRoot.Root));
        }

        [UnixFact]
        public async Task RunAsync_AProcessThatClosesItsPipeAndGoesOn_IsEndedWithItsTime()
        {
            ShelfFile file = await StoreClosingAsync();
            Task<MakingOutcome> making = MakeAsync(file);
            await Clock.WaitForTimerAsync(RunLimit);
            await SaysAsync("closed");

            Clock.Advance(RunLimit);

            Assert.Equal(MakingOutcome.Kept, await making);
            Assert.Null(Store.Find(DerivedKind.Thumbnail, file.Sha256)!.Made);
        }

        [UnixFact]
        public async Task StopAsync_OfAProcessThatClosedItsPipeAndGoesOn_EndsIt()
        {
            ShelfFile file = await StoreClosingAsync();
            Task<MakingOutcome> making = MakeAsync(file);
            await SaysAsync("closed");

            await Processes.StopAsync(file.Sha256).WaitAsync(RealTimeLimit);

            Assert.Equal(MakingOutcome.Stopped, await making);
            Assert.Empty(AllDerivedFiles());
        }

        [LinuxFact]
        public async Task RunAsync_ContentsThatCannotBeRead_RecordsNothing()
        {
            _program = Answering("[]", SayRead);
            ShelfFile file = await StoreTextAsync(await CreateShelfAsync(), "/a.txt", "hello");
            string path = ObjectStore.GetPath(file.Sha256);
            File.Delete(path);

            // Opens, and fails each read at its start.
            File.CreateSymbolicLink(path, "/proc/self/mem");

            Assert.Equal(MakingOutcome.NothingToMake, await MakeAsync(file));

            Assert.Empty(AllDerivedFiles());
        }

        [UnixFact]
        public async Task RunAsync_ContentsThatCannotBeNoted_AreNotGivenAndTheProcessEnds()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile before = await StoreTextAsync(shelf, "/before.txt", Plugins.Waiting("before"));
            ShelfFile now = await StoreTextAsync(shelf, "/now.txt", "now");
            using Process process = await RunAsync(before, "before");
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(Assert.Single(WorkFolder.Find(TestRoot.Root)).Path + WorkFolder.ContentsSuffix, UnixFileMode.UserRead);
            }

            await Assert.ThrowsAsync<PluginUnavailableException>(() => MakeAsync(now));

            Assert.True(process.HasExited);
            Assert.Single(Plugins.Runs);
            await EncryptInPlaceAsync(shelf, before);
            Assert.Equal("Thumbnail of now by test-maker 1", await GetAsync(shelf, now));
            Assert.Equal([now.Sha256], Assert.Single(WorkFolder.Find(TestRoot.Root)).GetContents());
        }

        [Fact]
        public async Task StopAllAsync_EndsWhatHasNothingToDo()
        {
            using Process process = await RunAsync(await StoreTextAsync(await CreateShelfAsync(), "/a.txt", Plugins.Waiting("a")), "a");

            await Processes.StopAllAsync().WaitAsync(RealTimeLimit);

            Assert.True(process.HasExited);
            Assert.Empty(WorkFolder.Find(TestRoot.Root));
        }

        [UnixTheory]
        [InlineData("large by path")]
        [InlineData("linked")]
        public async Task RunAsync_AFilePutInItsPlaceThatMayNotBeKept_HasFailedAlone(string contents)
        {
            ShelfFile file = await StoreTextAsync(await CreateShelfAsync(), "/a.txt", contents);

            Assert.Equal(MakingOutcome.Kept, await MakeAsync(file));

            Assert.Null(Store.Find(DerivedKind.Thumbnail, file.Sha256)!.Made);
            Assert.NotNull(Store.Find(DerivedKind.Preview, file.Sha256)!.Made);
        }

        /// <summary>Contents whose process closes its pipe once it has begun, and goes on.</summary>
        private async Task<ShelfFile> StoreClosingAsync()
        {
            // Once it has been answered, the server has heard that it started.
            _program = ShellProgram.Of(
                $"{ShellProgram.Say(FrameType.Ready)}; {ShellProgram.TakeRequest}; {ShellProgram.Say(FrameType.Started)}; {SayRead}; grep -aq hello; echo > closed; exec sleep 1000 >&-");
            return await StoreTextAsync(await CreateShelfAsync(), "/a.txt", "hello\n");
        }

        /// <summary>Waits until the process has made the file <paramref name="name"/> in its folder.</summary>
        private Task SaysAsync(string name)
        {
            return TestDerivedPlugins.UntilAsync(() => WorkFolder.Find(TestRoot.Root).Any(folder => File.Exists(Path.Combine(folder.Path, name))));
        }

        /// <summary>Asks for the start of the contents.</summary>
        private static string SayRead => ShellProgram.Say(FrameType.Read, Encoding.ASCII.GetString(new ReadRequest(0, 10).ToPayload()));

        private static PluginExecutable Answering(string answer, string script)
        {
            return ShellProgram.Of(
                $"{ShellProgram.Say(FrameType.Ready)}; {ShellProgram.TakeRequest}; {ShellProgram.Say(FrameType.Started)}; {script}; {ShellProgram.Say(FrameType.Answer, answer)}; sleep 1000");
        }

        /// <summary>Waits for something that kills processes, which those that will not end would hold up for as long as the clock stands.</summary>
        private async Task KillsAsync(Task killing)
        {
            using CancellationTokenSource limit = new CancellationTokenSource(KillPatience * 3);
            while (await Task.WhenAny(killing, Task.Delay(KillPatience, limit.Token)) != killing)
            {
                limit.Token.ThrowIfCancellationRequested();
                Clock.Advance(KillLimit);
            }

            await killing;
        }

        /// <summary>Has waiting contents made; answers the process that did it.</summary>
        private async Task<Process> RunAsync(ShelfFile file, string name)
        {
            Task<MakingOutcome> making = MakeAsync(file);
            Process process = await Plugins.StartedAsync(name);
            Plugins.GoOn(name);
            Assert.Equal(MakingOutcome.Kept, await making);
            return process;
        }
    }
}
