using ByteShelf.Server.Derived.Services;
using ByteShelf.Server.Tests.TestUtilities;

namespace ByteShelf.Server.Tests
{
    /// <summary>What the test assembly does as a program: a maker's child process, with the tests' own plugins to load.</summary>
    public static class TestProgram
    {
        public static int Main(string[] args)
        {
            return (int)MakeDerivedCommand.RunAsync(args.ElementAtOrDefault(1), TestMaker.Load).GetAwaiter().GetResult();
        }
    }
}
