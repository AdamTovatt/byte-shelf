using System.Runtime.InteropServices;
using ByteShelf.Server.Shared.Processes;
using ByteShelfCommon;

namespace ByteShelf.Server.Derived.Services
{
    /// <summary>
    /// <c>--make-derived &lt;folder&gt;</c>: the child process a server has a plugin's maker run in, for one
    /// contents. It does what <see cref="MakingFolder"/> asks and ends with the server that started it.
    /// </summary>
    internal static class MakeDerivedCommand
    {
        public const string Flag = "--make-derived";

        /// <summary>What a disk or a quota that is full is reported as: ENOSPC and EDQUOT on Linux and macOS, and Windows' two.</summary>
        private static readonly int[] DiskFull = OperatingSystem.IsWindows() ? [unchecked((int)0x80070027), unchecked((int)0x80070070)]
            : OperatingSystem.IsMacOS() ? [28, 69]
            : [28, 122];

        // Held so they are not collected, which would give the signals back their effect.
        private static readonly List<PosixSignalRegistration> IgnoredSignals = new List<PosixSignalRegistration>();

        /// <param name="folder">The <see cref="MakingFolder"/>.</param>
        /// <param name="load">Loads the maker at a location (<see cref="IDerivedFilePlugins.Load"/>).</param>
        public static async Task<MakingExitCode> RunAsync(string? folder, Func<string, IDerivedFileMaker?> load)
        {
            if (folder == null)
            {
                return MakingExitCode.WrongArguments;
            }

            ProcessGroup.Lead();
            LeaveStoppingToTheServer();
            EndWithTheServer();
            KeepMemoryOutOfCoreDumps();

            MakingFolder making = new MakingFolder(folder);
            if (making.ReadRequest() is not { } request)
            {
                return MakingExitCode.WrongArguments;
            }

            IDerivedFileMaker? maker = load(request.Location);
            if (maker == null || !request.Kinds.All(requested => maker.Makes.Any(declared => declared.Kind == requested.Kind && declared.Version == requested.Version)))
            {
                return MakingExitCode.PluginUnavailable;
            }

            FileStream contents;
            try
            {
                contents = File.OpenRead(request.ContentsPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return MakingExitCode.ContentsNotOpened;
            }

            making.SetStarted();
            IReadOnlyList<DerivedKind> kinds = request.Kinds.Select(requested => requested.Kind).ToList();
            IReadOnlyList<MadeFile> made;
            try
            {
                await using (contents)
                {
                    made = await maker.MakeAsync(contents, kinds, CancellationToken.None);
                }
            }
            catch (IOException exception) when (DiskFull.Contains(exception.HResult))
            {
                // Other I/O errors are as good as any: libraries throw them for damaged contents.
                return MakingExitCode.NotKept;
            }
            catch (Exception exception)
            {
                return Keep(() => making.WriteFailure(exception), MakingExitCode.Failed);
            }

            return Keep(
                () =>
                {
                    List<MadeFile> asked = made.Where(file => kinds.Contains(file.Kind)).DistinctBy(file => file.Kind).ToList();
                    foreach (MadeFile file in asked)
                    {
                        File.WriteAllBytes(making.PathOf(file.Kind), file.Contents);
                    }

                    making.WriteMade(asked.Select(file => new MadeInFolder(file.Kind, file.MediaType, file.Original)).ToList());
                },
                MakingExitCode.Made);
        }

        private static MakingExitCode Keep(Action write, MakingExitCode kept)
        {
            try
            {
                write();
                return kept;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A full disk, say, which is not the contents' doing.
                return MakingExitCode.NotKept;
            }
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

        /// <summary>
        /// Kills this process, with those it started, once the server's end of its standard input closes, which a
        /// server that is killed closes too: a maker that hangs would otherwise go on reading contents nobody
        /// knows it has.
        /// </summary>
        private static void EndWithTheServer()
        {
            Thread watch = new Thread(() =>
            {
                using Stream input = Console.OpenStandardInput();
                while (input.ReadByte() >= 0)
                {
                }

                ProcessGroup.KillOwn();
            });

            watch.IsBackground = true;
            watch.Start();
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
