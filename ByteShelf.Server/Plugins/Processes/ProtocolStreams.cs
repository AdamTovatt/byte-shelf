using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ByteShelf.Server.Plugins.Processes
{
    /// <summary>
    /// The standard input and output a plugin's process talks to the server over, taken from the process itself so
    /// that nothing a plugin or a native library reads or prints there is part of it. On Windows a native library
    /// that prints still is, which ends the run as a crash does.
    /// </summary>
    internal static class ProtocolStreams
    {
        public static (Stream Input, Stream Output) Take()
        {
            if (OperatingSystem.IsWindows())
            {
                Stream input = Console.OpenStandardInput();
                Stream output = Console.OpenStandardOutput();
                Console.SetIn(TextReader.Null);
                Console.SetOut(TextWriter.Null);
                return (input, output);
            }

            FileStream taken = Open(0, FileAccess.Read);
            FileStream takenOutput = Open(1, FileAccess.Write);
            using (SafeFileHandle nothing = File.OpenHandle("/dev/null", FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
            {
                foreach (int standard in new[] { 0, 1 })
                {
                    if (NativeMethods.Duplicate((int)nothing.DangerousGetHandle(), standard) < 0)
                    {
                        throw new InvalidOperationException("The process's standard streams could not be taken.");
                    }
                }
            }

            return (taken, takenOutput);
        }

        /// <summary>The pipe behind <paramref name="descriptor"/>, which a process a plugin starts does not inherit, as it would a duplicate.</summary>
        private static FileStream Open(int descriptor, FileAccess access)
        {
            return new FileStream($"/dev/fd/{descriptor}", FileMode.Open, access, FileShare.ReadWrite, bufferSize: 0);
        }

        private static class NativeMethods
        {
            [DllImport("libc", EntryPoint = "dup2")]
            [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
            public static extern int Duplicate(int descriptor, int onto);
        }
    }
}
