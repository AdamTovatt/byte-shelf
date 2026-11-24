using ByteShelf.Server.Derived.Services;

namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>A child process that is a shell script, for what no maker's process does by itself. It runs in its <see cref="MakingFolder"/>.</summary>
    public static class ShellProgram
    {
        /// <param name="script">What it does; its folder is the working folder.</param>
        public static MakerExecutable Of(string script)
        {
            return new MakerExecutable("/bin/sh", ["-c", script, "sh"]);
        }
    }
}
