using ByteShelf.Server.Shared.IO;
using ByteShelf.Server.Tests.TestUtilities;

namespace ByteShelf.Server.Tests.Shared
{
    /// <summary>Where a path leads.</summary>
    public sealed class FilePathsTests : IDisposable
    {
        private readonly string _folder = Path.Combine(AppContext.BaseDirectory, "file-paths", Guid.NewGuid().ToString("N"));

        public FilePathsTests()
        {
            Directory.CreateDirectory(Path.Combine(_folder, "real", "inner"));
        }

        [Theory]
        [InlineData("/a/b", "/a", true)]
        [InlineData("/a", "/a", true)]
        [InlineData("/a/..b", "/a", true)]
        [InlineData("/ab", "/a", false)]
        [InlineData("/a/../b", "/a", false)]
        [InlineData("/", "/a", false)]
        public void IsInside_ComparesWholeNames(string path, string folder, bool expected)
        {
            Assert.Equal(expected, FilePaths.IsInside(path, folder));
        }

        [UnixFact]
        public void ResolveFolder_LinksInEveryPart_AreResolved()
        {
            Directory.CreateSymbolicLink(In("first"), "second");
            Directory.CreateSymbolicLink(In("second"), In("real"));
            Directory.CreateSymbolicLink(Path.Combine(In("real"), "link"), "../first/inner");

            Assert.Equal(Path.Combine(In("real"), "inner"), FilePaths.ResolveFolder(Path.Combine(In("first"), "link") + "/"));
        }

        [UnixFact]
        public void ResolveFolder_LinksThatLeadInACircle_AreAnsweredAsWritten()
        {
            Directory.CreateSymbolicLink(In("one"), "two/inner");
            Directory.CreateSymbolicLink(In("two"), "one/inner");

            Assert.Equal(In("one"), FilePaths.ResolveFolder(In("one")));
            Assert.Equal(In("missing"), FilePaths.ResolveFolder(In("missing")));
        }

        public void Dispose()
        {
            Directory.Delete(_folder, recursive: true);
        }

        private string In(string name)
        {
            return Path.Combine(_folder, name);
        }
    }
}
