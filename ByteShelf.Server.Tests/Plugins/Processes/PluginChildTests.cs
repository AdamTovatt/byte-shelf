using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ByteShelf.Plugins;
using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Derived.Services;
using ByteShelf.Server.Plugins.Processes;
using ByteShelf.Server.Shared.Json;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.Plugins.Processes
{
    /// <summary>A plugin's child process by itself, spoken to as the server does.</summary>
    public sealed class PluginChildTests : IDisposable
    {
        private static readonly TimeSpan Patience = TimeSpan.FromMinutes(1);

        private readonly TestDerivedPlugins _plugins = new TestDerivedPlugins();
        private readonly string _folder = Directory.CreateTempSubdirectory("byteshelf-process-").FullName;
        private readonly List<Process> _started = new List<Process>();

        [LinuxFact]
        public async Task Run_LeavesItsMemoryOutOfCoreDumps()
        {
            Process process = await StartHangingAsync();

            Assert.Equal("00000000", (await File.ReadAllTextAsync($"/proc/{process.Id}/coredump_filter")).Trim());
        }

        [Fact]
        public async Task Run_OnceTheServersEndOfItsInputCloses_Ends()
        {
            Process process = await StartHangingAsync();
            Assert.False(process.HasExited);

            process.StandardInput.Close();

            Assert.NotEqual(0, await ExitCodeAsync(process));
        }

        [UnixFact]
        public async Task Run_OnceTheServersEndOfItsInputCloses_TakesWhatItStartedWithIt()
        {
            Process process = Start(TestDerivedPlugins.Executable, _plugins.Plugin().Target.Location);
            Task running = RunAsync(process, _plugins.WithChild("a", "wait"), DerivedKind.Thumbnail);
            int child = await OfAsync(_plugins.ChildOfAsync("a"), running);
            Assert.True(TestDerivedPlugins.IsRunning(child));

            process.StandardInput.Close();

            await TestDerivedPlugins.UntilAsync(() => !TestDerivedPlugins.IsRunning(child));
        }

        [LinuxFact]
        public async Task Run_WhatItStarts_HasNoneOfThePipesToTheServer()
        {
            Process process = Start(TestDerivedPlugins.Executable, _plugins.Plugin().Target.Location);
            Task running = RunAsync(process, _plugins.WithChild("a", "wait"), DerivedKind.Thumbnail);
            int child = await OfAsync(_plugins.ChildOfAsync("a"), running);

            IEnumerable<string?> open = new DirectoryInfo($"/proc/{child}/fd").EnumerateFileSystemInfos().Where(descriptor => descriptor.Name != "2").Select(descriptor => descriptor.LinkTarget);

            // Its standard error is this process's own, whatever that is.
            Assert.DoesNotContain(open, target => target!.StartsWith("pipe:", StringComparison.Ordinal));
            process.StandardInput.Close();
            await TestDerivedPlugins.UntilAsync(() => !TestDerivedPlugins.IsRunning(child));
        }

        [Fact]
        public async Task Run_AnsweredAsItStillReads_AnswersOnceItHasRead()
        {
            Process process = Start(TestDerivedPlugins.Executable, _plugins.Plugin().Target.Location);

            // More than it asks for at once, so that reading its start again is asked for.
            byte[] contents = Encoding.UTF8.GetBytes("reads on\n" + new string('x', 200_000));
            using CancellationTokenSource patience = new CancellationTokenSource(Patience);
            await AskAsync(process, contents.Length, DerivedKind.Thumbnail, patience.Token);
            Frame frame = await SayAsync(process, patience.Token);
            while (frame.Type != FrameType.Read || !File.Exists(Path.Combine(_folder, "thumbnail")))
            {
                if (frame.Type == FrameType.Read)
                {
                    await GiveAsync(process, contents, frame, patience.Token);
                }

                frame = await SayAsync(process, patience.Token);
            }

            Task<Frame> next = SayAsync(process, patience.Token);
            await Task.WhenAny(next, Task.Delay(TimeSpan.FromSeconds(1), patience.Token));
            Assert.False(next.IsCompleted);

            await GiveAsync(process, contents, frame, patience.Token);
            Assert.Equal(FrameType.Answer, (await next).Type);
        }

        [Theory]
        [InlineData("other kinds", typeof(ArgumentException))]
        [InlineData("kinds twice", typeof(InvalidOperationException))]
        public async Task Run_KindsTheMakerWasNotAskedForOrMakesTwice_AreRefused(string contents, Type refusal)
        {
            Process process = Start(TestDerivedPlugins.Executable, _plugins.Plugin().Target.Location);

            Frame answer = await RunAsync(process, contents, DerivedKind.Agent);

            Assert.Equal((FrameType.Failed, refusal.FullName), (answer.Type, Encoding.UTF8.GetString(answer.Payload)));
            Assert.Equal(TestMaker.Made(DerivedKind.Agent, contents), await File.ReadAllTextAsync(Path.Combine(_folder, "agent")));
            Assert.Equal([Path.Combine(_folder, "agent")], Directory.GetFileSystemEntries(_folder));
        }

        [Fact]
        public async Task Run_AFileLargerThanItMayBe_IsNotWrittenPastThat()
        {
            Process process = Start(TestDerivedPlugins.Executable, _plugins.Plugin().Target.Location);

            Frame answer = await RunAsync(process, "large thumbnail", DerivedKind.Thumbnail);

            Assert.Equal((FrameType.Answer, "[]"), (answer.Type, Encoding.UTF8.GetString(answer.Payload)));
            Assert.Equal(DerivedFileLimits.MaxBytesOf(DerivedKind.Thumbnail), new FileInfo(Path.Combine(_folder, "thumbnail")).Length);
        }

        [Fact]
        public async Task Run_SeveralOneAfterTheOther_AreAllAnswered()
        {
            Process process = Start(TestDerivedPlugins.Executable, _plugins.Plugin().Target.Location);

            FrameType[] answers =
            [
                (await RunAsync(process, "first", DerivedKind.Agent)).Type,
                (await RunAsync(process, "throw", DerivedKind.Agent)).Type,
                (await RunAsync(process, "last", DerivedKind.Agent)).Type,
            ];

            Assert.Equal([FrameType.Answer, FrameType.Failed, FrameType.Answer], answers);
            Assert.Equal("Agent of last by test-maker 1", await File.ReadAllTextAsync(Path.Combine(_folder, "agent")));
        }

        [Fact]
        public async Task Server_StartedAsAPluginsProcess_StartsNothingElse()
        {
            Process withoutALocation = Start(TestDerivedPlugins.ServerExecutable);
            Assert.Equal(PluginProcessCommand.WrongArguments, await ExitCodeAsync(withoutALocation));
            Process withAnotherFlag = Start(TestDerivedPlugins.ServerExecutable, "nowhere", "--highest-priority");
            Assert.Equal(PluginProcessCommand.WrongArguments, await ExitCodeAsync(withAnotherFlag));

            Process process = Start(TestDerivedPlugins.ServerExecutable, "nowhere");
            using CancellationTokenSource patience = new CancellationTokenSource(Patience);
            Frame? frame = await Frame.ReadAsync(process.StandardOutput.BaseStream, patience.Token);

            Assert.Equal(FrameType.Unavailable, frame!.Type);
            Assert.Empty(Directory.GetFileSystemEntries(_folder));
        }

        [Theory]
        [InlineData("/usr/bin/dotnet", "ByteShelf.Server.dll")]
        [InlineData("/srv/byteshelf/ByteShelf.Server", null)]
        public void PluginExecutable_OfAServer_IsItsOwnProgramOrDotnetWithItsAssembly(string process, string? argument)
        {
            PluginExecutable executable = PluginExecutable.Of(process, "/srv/byteshelf");

            Assert.Equal(process, executable.FileName);
            Assert.Equal(argument == null ? [] : [Path.Combine("/srv/byteshelf", argument)], executable.Arguments);
        }

        [Fact]
        public void PluginExecutable_OfThisServer_IsTheRunningProgram()
        {
            Assert.Equal(Environment.ProcessPath, PluginExecutable.OfThisServer().FileName);
        }

        [UnixFact]
        public async Task PluginMemory_AProcessOverItsLimit_IsFound()
        {
            Process process = Process.Start("sleep", "1000");
            _started.Add(process);
            using CancellationTokenSource patience = new CancellationTokenSource(Patience);

            Assert.True(await PluginMemory.ExceededAsync(process, maxBytes: 1, patience.Token));

            Assert.False(process.HasExited);
            Task<bool> within = PluginMemory.ExceededAsync(process, maxBytes: long.MaxValue, patience.Token);
            process.Kill();
            Assert.False(await within);
        }

        public void Dispose()
        {
            // Here, not at each test's end, so one whose assertions fail leaves no process running either.
            foreach (Process process in _started)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit();
                }

                process.Dispose();
            }

            Directory.Delete(_folder, recursive: true);
            _plugins.Dispose();
        }

        private static async Task<int> ExitCodeAsync(Process process)
        {
            using CancellationTokenSource patience = new CancellationTokenSource(Patience);
            await process.WaitForExitAsync(patience.Token);
            return process.ExitCode;
        }

        /// <summary>Has the process make <paramref name="kind"/> from <paramref name="text"/>, which it is given as it reads; answers the last it says.</summary>
        private static async Task<Frame> RunAsync(Process process, string text, DerivedKind kind)
        {
            byte[] contents = Encoding.UTF8.GetBytes(text);
            using CancellationTokenSource patience = new CancellationTokenSource(Patience);
            await AskAsync(process, contents.Length, kind, patience.Token);
            while (true)
            {
                Frame frame = await SayAsync(process, patience.Token);
                switch (frame.Type)
                {
                    case FrameType.Ready or FrameType.Started:
                        break;
                    case FrameType.Read:
                        await GiveAsync(process, contents, frame, patience.Token);
                        break;
                    default:
                        return frame;
                }
            }
        }

        private static async Task AskAsync(Process process, int length, DerivedKind kind, CancellationToken cancellationToken)
        {
            JsonElement arguments = JsonSerializer.SerializeToElement(new MakeArguments([new RequestedKind(kind, 1)]), JsonDefaults.CamelCase);
            byte[] request = JsonSerializer.SerializeToUtf8Bytes(new RunRequest(ProcessWork.NameOf(typeof(MakeWork)), arguments, length), JsonDefaults.CamelCase);
            await Frame.WriteAsync(process.StandardInput.BaseStream, FrameType.Run, request, cancellationToken);
        }

        /// <summary>What the process says next.</summary>
        private static async Task<Frame> SayAsync(Process process, CancellationToken cancellationToken)
        {
            return await Frame.ReadAsync(process.StandardOutput.BaseStream, cancellationToken) ?? throw new EndOfStreamException("The process ended.");
        }

        /// <summary>Gives the process what <paramref name="read"/> asks for.</summary>
        private static async Task GiveAsync(Process process, byte[] contents, Frame read, CancellationToken cancellationToken)
        {
            ReadRequest part = ReadRequest.From(read.Payload);
            await Frame.WriteAsync(process.StandardInput.BaseStream, FrameType.Bytes, contents.AsMemory((int)part.Offset, part.Count), cancellationToken);
        }

        /// <summary>Waits for <paramref name="awaited"/>, which a run that ends meanwhile never brings.</summary>
        private static async Task<T> OfAsync<T>(Task<T> awaited, Task running)
        {
            await OfAsync((Task)awaited, running);
            return await awaited;
        }

        private static async Task OfAsync(Task awaited, Task running)
        {
            await await Task.WhenAny(awaited, running);
            await awaited;
        }

        private async Task<Process> StartHangingAsync()
        {
            Process process = Start(TestDerivedPlugins.Executable, _plugins.Plugin().Target.Location);
            Task running = RunAsync(process, "hang", DerivedKind.Thumbnail);
            await OfAsync(TestDerivedPlugins.UntilAsync(() => _plugins.Runs.Count == 1), running);
            return process;
        }

        private Process Start(PluginExecutable executable, params string[] location)
        {
            ProcessStartInfo start = new ProcessStartInfo(executable.FileName, [.. executable.Arguments, PluginProcessCommand.Flag, .. location])
            {
                WorkingDirectory = _folder,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
            };

            Process process = Process.Start(start)!;
            _started.Add(process);
            return process;
        }
    }
}
