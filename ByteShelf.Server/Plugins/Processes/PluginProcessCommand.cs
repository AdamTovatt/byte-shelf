using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using ByteShelf.Server.Shared.Json;
using ByteShelf.Server.Shared.Processes;

namespace ByteShelf.Server.Plugins.Processes
{
    /// <summary>
    /// <c>--plugin-process &lt;location&gt;</c>: the child process a server has one plugin's work done in. It loads the
    /// plugin and does what the server asks (<see cref="Frame"/>), one thing at a time, until the server closes its
    /// input, with which it ends. It is given contents and a folder to write in, and none of the server's settings,
    /// services or files.
    /// </summary>
    internal sealed class PluginProcessCommand
    {
        public const string Flag = "--plugin-process";

        /// <summary>The process was started by something other than a server.</summary>
        public const int WrongArguments = 2;

        /// <summary>What a disk or a quota that is full is reported as: ENOSPC and EDQUOT on Linux and macOS, and Windows' two.</summary>
        private static readonly int[] DiskFull = OperatingSystem.IsWindows() ? [unchecked((int)0x80070027), unchecked((int)0x80070070)]
            : OperatingSystem.IsMacOS() ? [28, 69]
            : [28, 122];

        // Held so they are not collected, which would give the signals back their effect.
        private static readonly List<PosixSignalRegistration> IgnoredSignals = new List<PosixSignalRegistration>();

        private readonly Stream _input;
        private readonly Stream _output;
        private readonly string _folder = Environment.CurrentDirectory;
        private readonly Channel<Frame> _requests = Channel.CreateUnbounded<Frame>();
        private readonly Channel<byte[]> _parts = Channel.CreateUnbounded<byte[]>();

        private PluginProcessCommand(Stream input, Stream output)
        {
            _input = input;
            _output = output;
        }

        /// <summary>Runs it; does not return but for the exit code of a process that was not started as one.</summary>
        /// <param name="location">Where the plugin is (<see cref="PluginTarget.Location"/>).</param>
        /// <param name="load">Loads the plugin at a location, throwing when it cannot be.</param>
        public static async Task<int> RunAsync(string? location, Func<string, PluginWork> load)
        {
            if (location == null || !Console.IsInputRedirected)
            {
                return WrongArguments;
            }

            ProcessGroup.Lead();
            LeaveStoppingToTheServer();
            KeepMemoryOutOfCoreDumps();
            PluginMemory.LimitOwn();

            (Stream input, Stream output) = ProtocolStreams.Take();
            PluginProcessCommand process = new PluginProcessCommand(input, output);
            process.ListenUntilTheServerIsGone();

            PluginWork work;
            try
            {
                work = load(location);
            }
            catch (Exception exception)
            {
                // It has seen no contents that this could quote.
                process.Write(FrameType.Unavailable, $"it could not be loaded: {exception.Message}");
                await Task.Delay(Timeout.Infinite);
                throw;
            }

            process.Write(FrameType.Ready, string.Empty);
            while (true)
            {
                Frame request = await process._requests.Reader.ReadAsync();
                await process.DoAsync(work, JsonSerializer.Deserialize<RunRequest>(request.Payload, JsonDefaults.CamelCase)!);
            }
        }

        private async Task DoAsync(PluginWork work, RunRequest request)
        {
            Write(FrameType.Started, string.Empty);
            FrameType type;
            byte[] answer;
            PipedContents contents = new PipedContents(request.Length, Ask);
            try
            {
                JsonElement answered = await work(request.Work, request.Arguments, contents, _folder);
                (type, answer) = (FrameType.Answer, JsonSerializer.SerializeToUtf8Bytes(answered, JsonDefaults.CamelCase));
                if (answer.Length > Frame.MaxPayloadBytes)
                {
                    (type, answer) = (FrameType.Failed, Encoding.UTF8.GetBytes("its answer was too long"));
                }
            }
            catch (PluginUnavailableException exception)
            {
                (type, answer) = (FrameType.Unavailable, Encoding.UTF8.GetBytes(exception.Message));
            }
            catch (IOException exception) when (DiskFull.Contains(exception.HResult))
            {
                // Other I/O errors are as good as any: libraries throw them for damaged contents.
                (type, answer) = (FrameType.Unavailable, Encoding.UTF8.GetBytes("the disk is full"));
            }
            catch (Exception exception)
            {
                // By its type alone: a message may quote the contents.
                (type, answer) = (FrameType.Failed, Encoding.UTF8.GetBytes(exception.GetType().FullName!));
            }

            // Before the answer, after which the server sends no more of the contents.
            await contents.DisposeAsync();
            Frame.Write(_output, type, answer);
        }

        /// <summary>A part of the contents, as <see cref="PipedContents"/> asks for one at a time.</summary>
        private byte[] Ask(ReadRequest request)
        {
            Frame.Write(_output, FrameType.Read, request.ToPayload());
            return _parts.Reader.ReadAsync().AsTask().GetAwaiter().GetResult();
        }

        private void Write(FrameType type, string text)
        {
            Frame.Write(_output, type, Encoding.UTF8.GetBytes(text));
        }

        /// <summary>
        /// Takes what the server sends, on a thread of its own whatever the plugin is doing, and kills this process,
        /// with those it started, once the server's end closes, which a server that is killed closes too: a
        /// plugin that hangs would otherwise go on reading contents nobody knows it has.
        /// </summary>
        private void ListenUntilTheServerIsGone()
        {
            Thread listener = new Thread(() =>
            {
                try
                {
                    while (Frame.Read(_input) is { } frame)
                    {
                        if (frame.Type == FrameType.Bytes)
                        {
                            _parts.Writer.TryWrite(frame.Payload);
                        }
                        else
                        {
                            _requests.Writer.TryWrite(frame);
                        }
                    }
                }
                finally
                {
                    ProcessGroup.KillOwn();
                }
            });

            listener.IsBackground = true;
            listener.Start();
        }

        /// <summary>
        /// Ignores the signals a system that stops the server may send each of its processes: ended by one, a run
        /// would look like one its contents crashed. The server ends this process as it stops.
        /// </summary>
        private static void LeaveStoppingToTheServer()
        {
            foreach (PosixSignal signal in new[] { PosixSignal.SIGTERM, PosixSignal.SIGINT, PosixSignal.SIGQUIT, PosixSignal.SIGHUP })
            {
                IgnoredSignals.Add(PosixSignalRegistration.Create(signal, context => context.Cancel = true));
            }
        }

        /// <summary>Leaves the memory, and so what is read from the contents, out of a core dump the system may keep when this process crashes.</summary>
        private static void KeepMemoryOutOfCoreDumps()
        {
            if (OperatingSystem.IsLinux())
            {
                File.WriteAllText("/proc/self/coredump_filter", "0");
            }
        }
    }
}
