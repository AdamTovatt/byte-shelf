using ByteShelf.Server.Plugins.Processes;
using ByteShelf.Server.Tests.TestUtilities;

namespace ByteShelf.Server.Tests
{
    /// <summary>What the test assembly does as a program: a plugin's child process, with the tests' own plugins to load.</summary>
    public static class TestProgram
    {
        public static int Main(string[] args)
        {
            return PluginProcessCommand.RunAsync(
                args.ElementAtOrDefault(1),
                location => location == TestReader.Location ? TestReader.LoadWork() : TestMaker.LoadWork(location)).GetAwaiter().GetResult();
        }
    }
}
