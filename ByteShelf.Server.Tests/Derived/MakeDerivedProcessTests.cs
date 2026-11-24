using System.Diagnostics;
using System.Globalization;
using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Derived.Services;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.Derived
{
    /// <summary>A maker's child process by itself.</summary>
    public sealed class MakeDerivedProcessTests : IDisposable
    {
        private static readonly TimeSpan Patience = TimeSpan.FromMinutes(1);

        private readonly TestDerivedPlugins _plugins = new TestDerivedPlugins();
        private readonly MakingFolder _folder = new MakingFolder(Directory.CreateTempSubdirectory("byteshelf-making-").FullName);

        [Fact]
        public async Task Run_LeavesItsMemoryOutOfCoreDumps()
        {
            using Process process = await StartHangingAsync();

            if (OperatingSystem.IsLinux())
            {
                Assert.Equal("00000000", (await File.ReadAllTextAsync($"/proc/{process.Id}/coredump_filter")).Trim());
            }

            process.Kill();
        }

        [Fact]
        public async Task Run_OnceTheServersEndOfItsInputCloses_Ends()
        {
            using Process process = await StartHangingAsync();
            Assert.False(process.HasExited);

            process.StandardInput.Close();

            using CancellationTokenSource patience = new CancellationTokenSource(Patience);
            await process.WaitForExitAsync(patience.Token);
            Assert.NotEqual(0, process.ExitCode);
        }

        [UnixFact]
        public async Task Run_OnceTheServersEndOfItsInputCloses_TakesWhatItStartedWithIt()
        {
            using Process process = await StartAsync(_plugins.WithChild("a", "wait"));
            int child = await _plugins.ChildOfAsync("a");
            Assert.True(TestDerivedPlugins.IsRunning(child));

            process.StandardInput.Close();

            await TestDerivedPlugins.UntilAsync(() => !TestDerivedPlugins.IsRunning(child));
        }

        [Fact]
        public async Task Server_RunAsAMaker_AnswersItsExitCodeAndStartsNothing()
        {
            MakerExecutable server = new MakerExecutable(
                TestDerivedPlugins.Executable.FileName, [Path.Combine(AppContext.BaseDirectory, "ByteShelf.Server.dll")]);
            Assert.Equal(MakingExitCode.WrongArguments, await RunAsync(server, MakeDerivedCommand.Flag));
            Assert.Equal(MakingExitCode.WrongArguments, await RunAsync(server, MakeDerivedCommand.Flag, _folder.Path));

            _folder.WriteRequest(new MakingRequest("nowhere", "nothing", [new RequestedKind(DerivedKind.Thumbnail, 1)]));
            Assert.Equal(MakingExitCode.PluginUnavailable, await RunAsync(server, MakeDerivedCommand.Flag, _folder.Path));
            Assert.Single(Directory.EnumerateFileSystemEntries(_folder.Path));
        }

        [Fact]
        public async Task Run_KindsTheMakerWasNotAskedForOrAnswersTwice_AreNotKept()
        {
            using Process process = await StartAsync("other kinds", DerivedKind.Agent);
            using CancellationTokenSource patience = new CancellationTokenSource(Patience);
            await process.WaitForExitAsync(patience.Token);

            Assert.Equal((int)MakingExitCode.Made, process.ExitCode);
            Assert.Equal(DerivedKind.Agent, Assert.Single(_folder.ReadMade()!).Kind);
            Assert.Equal(
                (int)DerivedKind.Agent,
                int.Parse(await File.ReadAllTextAsync(_folder.PathOf(DerivedKind.Agent)), CultureInfo.InvariantCulture));
            Assert.All(DerivedKinds.All.Where(kind => kind != DerivedKind.Agent), kind => Assert.False(File.Exists(_folder.PathOf(kind))));
        }

        [Theory]
        [InlineData("/usr/bin/dotnet", "/srv/byteshelf/ByteShelf.Server.dll")]
        [InlineData("/srv/byteshelf/ByteShelf.Server", null)]
        public void MakerExecutable_OfAServer_IsItsOwnProgramOrDotnetWithItsAssembly(string process, string? argument)
        {
            MakerExecutable executable = MakerExecutable.Of(process, "/srv/byteshelf");

            Assert.Equal(process, executable.FileName);
            Assert.Equal(argument == null ? [] : [Path.Combine("/srv/byteshelf", Path.GetFileName(argument))], executable.Arguments);
        }

        [Fact]
        public void MakerExecutable_OfThisServer_IsTheRunningProgram()
        {
            Assert.Equal(Environment.ProcessPath, MakerExecutable.OfThisServer().FileName);
        }

        public void Dispose()
        {
            _folder.Remove();
            _plugins.Dispose();
        }

        private async Task<Process> StartHangingAsync()
        {
            Process process = await StartAsync("hang");
            await TestDerivedPlugins.UntilAsync(() => _plugins.Runs.Count == 1);
            return process;
        }

        /// <summary>Starts one that makes <paramref name="kind"/> from <paramref name="text"/>.</summary>
        private async Task<Process> StartAsync(string text, DerivedKind kind = DerivedKind.Thumbnail)
        {
            string contents = Path.Combine(_folder.Path, "contents");
            await File.WriteAllTextAsync(contents, text);
            _folder.WriteRequest(new MakingRequest(_plugins.Plugin().Location, contents, [new RequestedKind(kind, 1)]));
            return Start(TestDerivedPlugins.Executable, MakeDerivedCommand.Flag, _folder.Path);
        }

        private async Task<MakingExitCode> RunAsync(MakerExecutable executable, params string[] arguments)
        {
            using Process process = Start(executable, arguments);
            using CancellationTokenSource patience = new CancellationTokenSource(Patience);
            await process.WaitForExitAsync(patience.Token);
            return (MakingExitCode)process.ExitCode;
        }

        private Process Start(MakerExecutable executable, params string[] arguments)
        {
            ProcessStartInfo start = new ProcessStartInfo(executable.FileName, [.. executable.Arguments, .. arguments])
            {
                WorkingDirectory = _folder.Path,
                RedirectStandardInput = true,
            };

            return Process.Start(start)!;
        }
    }
}
