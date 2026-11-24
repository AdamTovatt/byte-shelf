using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace ByteShelf.Server.Shared.Processes
{
    /// <summary>
    /// A child process with every process it starts, on Unix: the child leads a group, which outlives it, so what
    /// it started can be ended though it is gone itself. Not on Windows, where only a live process knows its children.
    /// </summary>
    internal static class ProcessGroup
    {
        private const int KillSignal = 9;

        private static bool _leads;

        /// <summary>
        /// Has this process lead a group (and session) of its own, so a terminal's signals to its parent's do not
        /// reach it either.
        /// </summary>
        public static void Lead()
        {
            _leads = !OperatingSystem.IsWindows() && NativeMethods.SetSid() >= 0;
        }

        /// <summary>Kills this process, and the group it leads.</summary>
        public static void KillOwn()
        {
            if (_leads)
            {
                _ = NativeMethods.Kill(0, KillSignal);
            }

            Process.GetCurrentProcess().Kill();
        }

        /// <summary>Kills every process in the group <paramref name="leader"/> leads or led, if there is one.</summary>
        public static void Kill(int leader)
        {
            if (!OperatingSystem.IsWindows())
            {
                _ = NativeMethods.Kill(-leader, KillSignal);
            }
        }

        /// <summary>Whether no process is left running in the group <paramref name="leader"/> led.</summary>
        public static bool IsGone(int leader)
        {
            if (OperatingSystem.IsWindows() || NativeMethods.Kill(-leader, 0) != 0)
            {
                return true;
            }

            // A killed process nobody has reaped still counts as one above, and in a container nobody may.
            return OperatingSystem.IsLinux() && !Directory.EnumerateDirectories("/proc").Any(folder => IsRunningIn(folder, leader));
        }

        private static bool IsRunningIn(string processFolder, int group)
        {
            try
            {
                // "pid (name) state parent group ...", where the name may hold anything.
                string stat = File.ReadAllText(Path.Combine(processFolder, "stat"));
                string[] fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
                return fields[0] is not ("Z" or "X") && int.Parse(fields[2], CultureInfo.InvariantCulture) == group;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // No process's folder, or one that ended just now.
                return false;
            }
        }

        private static class NativeMethods
        {
            [DllImport("libc", EntryPoint = "setsid")]
            [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
            public static extern int SetSid();

            [DllImport("libc", EntryPoint = "kill")]
            [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
            public static extern int Kill(int process, int signal);
        }
    }
}
