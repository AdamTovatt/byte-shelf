using System.Text;
using ByteShelf.Server.Plugins.Processes;

namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>A plugin's process that is a shell script, for what none does by itself. It runs in its folder.</summary>
    internal static class ShellProgram
    {
        /// <summary>Waits for what the server asks next, whatever it is: that it has asked is enough here.</summary>
        public const string TakeRequest = "head -c 1 > /dev/null";

        /// <param name="script">What it does.</param>
        public static PluginExecutable Of(string script)
        {
            return new PluginExecutable("/bin/sh", ["-c", script, "sh"]);
        }

        /// <summary>Says something to the server, as the process does.</summary>
        public static string Say(FrameType type, string payload = "")
        {
            using MemoryStream frame = new MemoryStream();
            Frame.Write(frame, type, Encoding.UTF8.GetBytes(payload));
            return $"printf '{string.Concat(frame.ToArray().Select(value => $"\\{Convert.ToString(value, 8).PadLeft(3, '0')}"))}'";
        }
    }
}
