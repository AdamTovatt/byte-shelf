using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace ByteShelf.Server.Plugins.Processes
{
    /// <summary>
    /// How much memory a plugin's process may take, so contents that decode to more end it and not the server. On
    /// Linux the process limits itself before it loads the plugin, and cannot take more; elsewhere the server
    /// watches it, and it may for a moment.
    /// </summary>
    internal static class PluginMemory
    {
        /// <summary>1.5 GiB: what decoding one large image takes, with the runtime.</summary>
        public const long MaxBytes = 1536L * 1024 * 1024;

        /// <summary>
        /// What a process may still take once it has answered, and go on: one that takes more would leave the next
        /// contents too little, which would be taken for their doing.
        /// </summary>
        public const long MaxKeptBytes = 512L * 1024 * 1024;

        private static readonly TimeSpan WatchInterval = TimeSpan.FromMilliseconds(100);

        /// <summary>Whether a process limits itself (<see cref="LimitOwn"/>); otherwise it is watched (<see cref="ExceededAsync"/>).</summary>
        public static bool LimitsItself => OperatingSystem.IsLinux();

        /// <summary>Limits this process, where it can.</summary>
        public static void LimitOwn()
        {
            if (LimitsItself)
            {
                NativeMethods.Limit limit = new NativeMethods.Limit { Current = MaxBytes, Maximum = MaxBytes };
                if (NativeMethods.SetLimit(NativeMethods.DataLimit, in limit) != 0)
                {
                    throw new InvalidOperationException("The process's memory could not be limited.");
                }
            }
        }

        /// <summary>Ends once <paramref name="process"/> takes more than <paramref name="maxBytes"/>, on the real clock, answering whether it does, or has ended.</summary>
        public static async Task<bool> ExceededAsync(Process process, long maxBytes, CancellationToken cancellationToken)
        {
            while (!process.HasExited)
            {
                if (Exceeds(process, maxBytes))
                {
                    return true;
                }

                await Task.Delay(WatchInterval, cancellationToken);
                process.Refresh();
            }

            return false;
        }

        /// <summary>Whether <paramref name="process"/>, which has answered, takes more than <see cref="MaxKeptBytes"/>.</summary>
        public static bool KeepsTooMuch(Process process)
        {
            if (!OperatingSystem.IsLinux())
            {
                process.Refresh();
                return Exceeds(process, MaxKeptBytes);
            }

            try
            {
                // What the limit counts: "VmData:	   51792 kB".
                string line = File.ReadLines($"/proc/{process.Id}/status").First(line => line.StartsWith("VmData:", StringComparison.Ordinal));
                return long.Parse(line.Split(':', StringSplitOptions.TrimEntries)[1].Split(' ')[0], CultureInfo.InvariantCulture) * 1024 > MaxKeptBytes;
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                // It ended just now.
                return true;
            }
        }

        private static bool Exceeds(Process process, long maxBytes)
        {
            try
            {
                return process.WorkingSet64 > maxBytes;
            }
            catch (InvalidOperationException)
            {
                // It ended just now.
                return false;
            }
        }

        private static class NativeMethods
        {
            public const int DataLimit = 2;

            [DllImport("libc", EntryPoint = "setrlimit")]
            [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
            public static extern int SetLimit(int resource, in Limit limit);

            [StructLayout(LayoutKind.Sequential)]
            public struct Limit
            {
                public ulong Current;
                public ulong Maximum;
            }
        }
    }
}
