using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ByteShelf.Server.Shared.Json;
using ByteShelf.Server.Shared.Processes;
using Microsoft.Win32.SafeHandles;

namespace ByteShelf.Server.Plugins.Processes
{
    /// <summary>One running child process of a plugin, with every process it starts.</summary>
    internal sealed class PluginProcess : IDisposable
    {
        /// <summary>How long killed processes may take to be gone.</summary>
        private static readonly TimeSpan KillLimit = TimeSpan.FromSeconds(5);

        /// <summary>How often it is looked whether what a process started is gone, on the real clock: first, and at the least.</summary>
        private static readonly TimeSpan FirstGoneInterval = TimeSpan.FromMilliseconds(10);
        private static readonly TimeSpan LastGoneInterval = TimeSpan.FromSeconds(1);

        /// <summary>The server's environment a child gets: where the runtime is and how it behaves, and nothing the server is set up by.</summary>
        private static readonly string[] PassedVariables = ["PATH", "LANG", "LANGUAGE", "TZ", "SystemRoot", "windir"];
        private static readonly string[] PassedPrefixes = ["LC_", "DOTNET_"];

        private readonly Process _process;
        private readonly int _id;
        private readonly TimeProvider _timeProvider;

        /// <summary>Ends once the process has and nothing it started is left. From then on its id may be another's.</summary>
        private readonly Task _gone;

        private PluginProcess(Process process, WorkFolder folder, TimeProvider timeProvider)
        {
            _process = process;
            _id = process.Id;
            _timeProvider = timeProvider;
            Folder = folder;
            _gone = KillGroupOnceExitedAsync();
        }

        public WorkFolder Folder { get; }

        /// <summary>Starts one of <paramref name="plugin"/> for <paramref name="lane"/> in <paramref name="folder"/>.</summary>
        /// <exception cref="PluginUnavailableException">It could not be started.</exception>
        public static PluginProcess Start(PluginExecutable executable, PluginTarget plugin, PluginLane lane, WorkFolder folder, TimeProvider timeProvider)
        {
            ProcessStartInfo start = new ProcessStartInfo(executable.FileName, [.. executable.Arguments, .. PluginProcessCommand.ArgumentsFor(plugin, lane)])
            {
                WorkingDirectory = folder.Path,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            SetEnvironment(start.Environment, folder);
            try
            {
                Process process = Process.Start(start) ?? throw new PluginUnavailableException($"{executable.FileName} could not be started");

                // Read so it never waits on a full pipe, and thrown away: it may quote the contents.
                _ = process.StandardError.BaseStream.CopyToAsync(Stream.Null, CancellationToken.None);
                return new PluginProcess(process, folder, timeProvider);
            }
            catch (System.ComponentModel.Win32Exception exception)
            {
                throw new PluginUnavailableException($"{executable.FileName} could not be started", exception);
            }
        }

        /// <summary>Waits until the plugin is loaded. The process is to be ended when this throws.</summary>
        /// <exception cref="PluginUnavailableException">It is not.</exception>
        public async Task LoadedAsync(TimeSpan limit, CancellationToken cancellationToken)
        {
            using CancellationTokenSource time = new CancellationTokenSource(limit, _timeProvider);
            using CancellationTokenSource either = CancellationTokenSource.CreateLinkedTokenSource(time.Token, cancellationToken);
            using CancellationTokenRegistration killed = either.Token.Register(Kill);
            try
            {
                Frame? frame = await Frame.ReadAsync(_process.StandardOutput.BaseStream, either.Token);
                either.Token.ThrowIfCancellationRequested();
                switch (frame?.Type)
                {
                    case FrameType.Ready:
                        return;
                    case FrameType.Unavailable:
                        throw new PluginUnavailableException(Encoding.UTF8.GetString(frame.Payload));
                    default:
                        throw new PluginUnavailableException("its process ended before it was loaded");
                }
            }
            catch (InvalidDataException exception)
            {
                throw new PluginUnavailableException("its process said something else before it was loaded", exception);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new PluginUnavailableException($"it took over {limit} to load");
            }
        }

        /// <summary>
        /// Has the plugin do <paramref name="request"/> over <paramref name="contents"/>. Null when the process ended, or was for
        /// its time, before it began, which says nothing of them. The process may do more when it answered and gave back the memory
        /// it took; otherwise, and when this throws, it is to be ended.
        /// </summary>
        /// <exception cref="PluginUnavailableException">The plugin cannot do it, whatever the contents.</exception>
        /// <exception cref="OperationCanceledException">It was stopped, which says nothing of the contents.</exception>
        public async Task<(PluginRunResult? Result, bool GoesOn)> RunAsync(RunRequest request, SafeFileHandle contents, TimeSpan limit, CancellationToken cancellationToken)
        {
            using CancellationTokenSource time = new CancellationTokenSource(limit, _timeProvider);
            using CancellationTokenSource either = CancellationTokenSource.CreateLinkedTokenSource(time.Token, cancellationToken);

            // Killed, its pipes end, which ends what waits for them where a token cannot, as on Windows.
            using CancellationTokenRegistration killed = either.Token.Register(Kill);
            using CancellationTokenSource watched = CancellationTokenSource.CreateLinkedTokenSource(either.Token);
            Task<bool> overMemory = PluginMemory.LimitsItself ? Task.FromResult(false) : KillOnceOverMemoryAsync(watched.Token);
            bool started = false;
            try
            {
                try
                {
                    Stream output = _process.StandardOutput.BaseStream;
                    await WriteAsync(FrameType.Run, JsonSerializer.SerializeToUtf8Bytes(request, JsonDefaults.CamelCase), either.Token);
                    while (await Frame.ReadAsync(output, either.Token) is { } frame)
                    {
                        switch (frame.Type)
                        {
                            case FrameType.Started:
                                started = true;
                                break;
                            case FrameType.Read:
                                if (await ReadAsync(contents, ReadRequest.From(frame.Payload), either.Token) is not { } part)
                                {
                                    return (new PluginRunResult.ContentsNotRead(), GoesOn: false);
                                }

                                await WriteAsync(FrameType.Bytes, part, either.Token);
                                break;
                            case FrameType.Answer:
                                return (new PluginRunResult.Answered(JsonSerializer.Deserialize<JsonElement>(frame.Payload)), GoesOn: !PluginMemory.KeepsTooMuch(_process));
                            case FrameType.Failed:
                                // What made it throw, such as memory it ran out of, may be there for the next contents too.
                                return (new PluginRunResult.Failed(Encoding.UTF8.GetString(frame.Payload)), GoesOn: false);
                            case FrameType.Unavailable:
                                throw new PluginUnavailableException(Encoding.UTF8.GetString(frame.Payload));
                            default:
                                return (new PluginRunResult.Failed("its process said what it may not"), GoesOn: false);
                        }
                    }
                }
                catch (IOException)
                {
                    // The pipe broke, as its other end is gone.
                }

                // It may have closed its pipe and gone on.
                await _process.WaitForExitAsync(either.Token);
                either.Token.ThrowIfCancellationRequested();
                return (started ? new PluginRunResult.Failed(await overMemory
                    ? $"it took over {PluginMemory.MaxBytes} bytes of memory"
                    : $"its process ended with exit code {_process.ExitCode}") : null, GoesOn: false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return (started ? new PluginRunResult.Failed($"it took over {limit}") : null, GoesOn: false);
            }
            catch (Exception exception) when (exception is InvalidDataException or JsonException)
            {
                return (new PluginRunResult.Failed("its process said what it may not"), GoesOn: false);
            }
            finally
            {
                await watched.CancelAsync();
                await ((Task)overMemory).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }

        /// <summary>Kills what is left of it and of what it started, and waits until they are gone.</summary>
        /// <exception cref="PluginNotStoppedException">They are still there.</exception>
        public async Task EndAsync()
        {
            Kill();
            try
            {
                await _gone.WaitAsync(KillLimit, _timeProvider);
            }
            catch (Exception exception)
            {
                throw new PluginNotStoppedException($"The process {_id}, or one it started, was killed and did not end.", exception);
            }
        }

        public void Dispose()
        {
            _process.Dispose();
        }

        private static void SetEnvironment(IDictionary<string, string?> environment, WorkFolder folder)
        {
            foreach (string variable in environment.Keys.ToList())
            {
                if (!PassedVariables.Contains(variable, StringComparer.OrdinalIgnoreCase)
                    && !PassedPrefixes.Any(prefix => variable.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                {
                    environment.Remove(variable);
                }
            }

            // Where libraries put temporary files, on every platform.
            foreach (string variable in new[] { "TMPDIR", "TMP", "TEMP" })
            {
                environment[variable] = folder.Path;
            }

            // No dump of its memory, whatever the server's environment says.
            environment["DOTNET_DbgEnableMiniDump"] = "0";
            environment["DOTNET_EnableDiagnostics"] = "0";

            // The collector knows nothing of the memory limit, and one that runs beside the plugin lets garbage reach it.
            environment["DOTNET_gcConcurrent"] = "0";
        }

        private static async Task<byte[]?> ReadAsync(SafeFileHandle contents, ReadRequest request, CancellationToken cancellationToken)
        {
            byte[] part = new byte[request.Count];
            try
            {
                int read = 0;
                while (read < part.Length && await RandomAccess.ReadAsync(contents, part.AsMemory(read), request.Offset + read, cancellationToken) is > 0 and int more)
                {
                    read += more;
                }

                return part[..read];
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        private ValueTask WriteAsync(FrameType type, byte[] payload, CancellationToken cancellationToken)
        {
            return Frame.WriteAsync(_process.StandardInput.BaseStream, type, payload, cancellationToken);
        }

        /// <summary>Kills it, with what it started, unless it has ended. Its group is killed too once it has.</summary>
        private void Kill()
        {
            try
            {
                if (!_process.HasExited)
                {
                    // Not its group, which it may have had no time to lead, and Windows has none.
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or AggregateException)
            {
                // It ended just now, or cannot be killed, as whoever waits for its end finds.
            }
        }

        /// <summary>Kills it once it takes too much memory; answers whether it did before it ended.</summary>
        private async Task<bool> KillOnceOverMemoryAsync(CancellationToken cancellationToken)
        {
            bool exceeded = await PluginMemory.ExceededAsync(_process, PluginMemory.MaxBytes, cancellationToken);
            if (exceeded)
            {
                Kill();
            }

            return exceeded;
        }

        /// <summary>What it started may hold the other end of its output, which then never ends by itself.</summary>
        private async Task KillGroupOnceExitedAsync()
        {
            await _process.WaitForExitAsync(CancellationToken.None);
            ProcessGroup.Kill(_id);
            for (TimeSpan interval = FirstGoneInterval; !ProcessGroup.IsGone(_id); interval = interval * 2 < LastGoneInterval ? interval * 2 : LastGoneInterval)
            {
                await Task.Delay(interval);
            }
        }
    }
}
