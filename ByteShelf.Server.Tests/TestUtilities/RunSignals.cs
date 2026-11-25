using System.Globalization;

namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>What a run in a plugin's process tells the test (<see cref="TestDerivedPlugins"/>), by files at a path it is given.</summary>
    internal static class RunSignals
    {
        /// <summary>Says that it has started, at path + ".started" with the process's id, and waits until there is a file at the path.</summary>
        public static async Task WaitAsync(string path)
        {
            Say(path + ".started", Environment.ProcessId);
            while (!File.Exists(path))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(5));
            }
        }

        /// <summary>Writes the id of a process to a file, whole from the start.</summary>
        public static void Say(string path, int processId)
        {
            File.WriteAllText(path + ".tmp", processId.ToString(CultureInfo.InvariantCulture));
            File.Move(path + ".tmp", path, overwrite: true);
        }
    }
}
