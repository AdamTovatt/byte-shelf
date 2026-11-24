using System.Diagnostics;
using ByteShelf.Server.Shared.Processes;

namespace ByteShelf.Server.Derived.Services
{
    /// <summary>One child process running a plugin's maker, with every process it starts.</summary>
    internal sealed class MakerProcess : IDisposable
    {
        /// <summary>How long killed processes may take to be gone.</summary>
        private static readonly TimeSpan KillLimit = TimeSpan.FromSeconds(5);

        /// <summary>How often it is looked whether what a process started is gone, on the real clock.</summary>
        private static readonly TimeSpan GoneInterval = TimeSpan.FromMilliseconds(10);

        private readonly Process _process;
        private readonly int _id;
        private readonly TimeProvider _timeProvider;

        private MakerProcess(Process process, TimeProvider timeProvider)
        {
            _process = process;
            _id = process.Id;
            _timeProvider = timeProvider;
        }

        /// <summary>Starts one that does what <paramref name="folder"/> asks.</summary>
        /// <exception cref="MakerUnavailableException">It could not be started.</exception>
        public static MakerProcess Start(MakerExecutable executable, MakingFolder folder, TimeProvider timeProvider)
        {
            ProcessStartInfo start = new ProcessStartInfo(executable.FileName, [.. executable.Arguments, MakeDerivedCommand.Flag, folder.Path])
            {
                WorkingDirectory = folder.Path,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            // Where libraries put temporary files, on every platform.
            foreach (string variable in new[] { "TMPDIR", "TMP", "TEMP" })
            {
                start.Environment[variable] = folder.Path;
            }

            // No dump of its memory, whatever the server's environment says.
            start.Environment["DOTNET_DbgEnableMiniDump"] = "0";
            start.Environment["DOTNET_EnableDiagnostics"] = "0";

            Process process;
            try
            {
                process = Process.Start(start) ?? throw new MakerUnavailableException($"{executable.FileName} could not be started");
            }
            catch (System.ComponentModel.Win32Exception exception)
            {
                throw new MakerUnavailableException($"{executable.FileName} could not be started", exception);
            }

            // Read so it never waits on a full pipe, and thrown away: it may quote the contents.
            _ = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null, CancellationToken.None);
            _ = process.StandardError.BaseStream.CopyToAsync(Stream.Null, CancellationToken.None);
            return new MakerProcess(process, timeProvider);
        }

        /// <summary>Its exit code; null when it did not end by itself within <paramref name="limit"/>.</summary>
        public async Task<int?> ExitsWithinAsync(TimeSpan limit, CancellationToken cancellationToken)
        {
            try
            {
                await _process.WaitForExitAsync(CancellationToken.None).WaitAsync(limit, _timeProvider, cancellationToken);
                return _process.ExitCode;
            }
            catch (TimeoutException)
            {
                return null;
            }
        }

        /// <summary>Kills what is left of it and of what it started, whether it ended or not, and waits until they are gone.</summary>
        /// <exception cref="MakerNotStoppedException">They are still there.</exception>
        public async Task EndAsync()
        {
            try
            {
                if (!_process.HasExited)
                {
                    // Not only its group, which it may have had no time to lead, and Windows has none.
                    _process.Kill(entireProcessTree: true);
                }

                ProcessGroup.Kill(_id);
                using CancellationTokenSource limit = new CancellationTokenSource(KillLimit, _timeProvider);
                await _process.WaitForExitAsync(limit.Token);
                while (!ProcessGroup.IsGone(_id))
                {
                    await Task.Delay(GoneInterval, limit.Token);
                }
            }
            catch (Exception exception)
            {
                throw new MakerNotStoppedException($"The process {_id}, or one it started, was killed and did not end.", exception);
            }
        }

        public void Dispose()
        {
            _process.Dispose();
        }
    }
}
