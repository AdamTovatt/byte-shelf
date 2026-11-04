using ByteShelf.Cli.Commands;
using ByteShelf.Tools;

namespace ByteShelf.Cli.Tests
{
    /// <summary>How a command line's words are read (<see cref="ShelfLocation"/>, <see cref="ArgumentList"/>) and how sizes print.</summary>
    public class ArgumentsTests
    {
        [Theory]
        [InlineData("docs", "/", true)]
        [InlineData("docs:", "/", true)]
        [InlineData("docs:/", "/", true)]
        [InlineData("docs:/a/b", "/a/b", false)]
        [InlineData("docs:/a/b/", "/a/b", true)]
        [InlineData("docs:a/b", "/a/b", false)]
        public void ShelfLocation_Parse_SplitsShelfAndPath(string text, string path, bool namesADirectory)
        {
            ShelfLocation location = ShelfLocation.Parse(text);

            Assert.Equal(("docs", path, namesADirectory), (location.Shelf, location.Path, location.NamesADirectory));
        }

        [Fact]
        public void ShelfLocation_PathWithAColon_KeepsItInThePath()
        {
            Assert.Equal("/at 10:30.txt", ShelfLocation.Parse("docs:/at 10:30.txt").Path);
        }

        [Theory]
        [InlineData("Docs:/a")]
        [InlineData(":/a")]
        [InlineData("docs:/a/../b")]
        public void ShelfLocation_NotAllowed_ThrowsUsage(string text)
        {
            Assert.Throws<UsageException>(() => ShelfLocation.Parse(text));
        }

        [Fact]
        public void ShelfLocation_FilePathFor_PutsTheNameInsideADirectory()
        {
            Assert.Equal(
                ("/inbox/a.pdf", "/b.pdf"),
                (ShelfLocation.Parse("docs:/inbox/").FilePathFor("a.pdf"), ShelfLocation.Parse("docs:/b.pdf").FilePathFor("a.pdf")));
        }

        [Theory]
        [InlineData("b.txt", "/b.txt")]
        [InlineData("/a/b.txt", "/a/b.txt")]
        [InlineData("a/b:c", "/a/b:c")]
        [InlineData("/a:b", "/a:b")]
        [InlineData("docs:/a/b.txt", "/a/b.txt")]
        [InlineData("docs:", "/")]
        public void ShelfLocation_PathOn_ReadsAPathOrTheSameShelfsPath(string text, string path)
        {
            Assert.Equal(path, ShelfLocation.PathOn("docs", text));
        }

        [Theory]
        [InlineData("photos:/a.txt")]
        [InlineData("Meeting: notes.txt")]
        [InlineData("docs:a//b")]
        public void ShelfLocation_PathOn_AnotherShelfOrNoneOrABadPath_ThrowsUsage(string text)
        {
            Assert.Throws<UsageException>(() => ShelfLocation.PathOn("docs", text));
        }

        [Fact]
        public void ShelfLocation_PathOn_NotAShelfBeforeTheColon_SaysHowToKeepTheColon()
        {
            UsageException refused = Assert.Throws<UsageException>(() => ShelfLocation.PathOn("docs", "Meeting: notes.txt"));

            Assert.Equal("'Meeting: notes.txt' reads as <shelf>:<path>, and 'Meeting' is not a shelf name. Start it with / to keep the colon in a name: '/Meeting: notes.txt'.", refused.Message);
        }

        [Fact]
        public void ArgumentList_ReadsPositionalsFlagsAndValues()
        {
            ArgumentList arguments = ArgumentList.Parse(new[] { "docs", "--quota", "100", "--overwrite", "x" });

            Assert.Equal(("docs", "x", 100L, true), (arguments.Required(0, "a"), arguments.Required(1, "b"), arguments.Bytes("--quota"), arguments.Flag("--overwrite")));
            arguments.EnsureAllUsed();
        }

        [Fact]
        public void ArgumentList_AfterDoubleDash_EverythingIsPositional()
        {
            ArgumentList arguments = ArgumentList.Parse(new[] { "--", "--weird-name" });

            Assert.Equal("--weird-name", arguments.Required(0, "name"));
            arguments.EnsureAllUsed();
        }

        [Theory]
        [InlineData("--quota")]
        [InlineData("--overwrite", "--overwrite")]
        public void ArgumentList_ValueMissingOrOptionTwice_ThrowsUsage(params string[] words)
        {
            Assert.Throws<UsageException>(() => ArgumentList.Parse(words));
        }

        [Theory]
        [InlineData("ten")]
        [InlineData("-5")]
        [InlineData("1.5")]
        public void ArgumentList_QuotaNotAWholeNumberOfBytes_ThrowsUsage(string quota)
        {
            Assert.Throws<UsageException>(() => ArgumentList.Parse(new[] { "--quota", quota }).Bytes("--quota"));
        }

        [Fact]
        public void ShelfLocation_Of_TakesTheShelfAndPathApart()
        {
            ShelfLocation location = ShelfLocation.Of("docs", "/inbox/");

            Assert.Equal(("docs", "/inbox", true), (location.Shelf, location.Path, location.NamesADirectory));
        }

        [Theory]
        [InlineData(0L, "0 B")]
        [InlineData(1023L, "1023 B")]
        [InlineData(1024L, "1 KiB")]
        [InlineData(1536L, "1.5 KiB")]
        [InlineData(1048576L, "1 MiB")]
        public void TextFormat_Size_SwitchesUnitAt1024(long bytes, string expected)
        {
            Assert.Equal(expected, TextFormat.Size(bytes));
        }

        [Theory]
        [InlineData("docs", "extra")]
        [InlineData("docs", "--unknown")]
        public void ArgumentList_SomethingUnread_ThrowsUsage(params string[] words)
        {
            ArgumentList arguments = ArgumentList.Parse(words);
            arguments.Required(0, "shelf");

            Assert.Throws<UsageException>(arguments.EnsureAllUsed);
        }
    }
}
