using ByteShelfClient;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.Client
{
    /// <summary>Listing a directory's tree, the same in both clients.</summary>
    public abstract partial class ClientContractTests
    {
        [Fact]
        public async Task ListTreeAsync_WholeShelf_ListsLiveEntriesAtEveryLevelByPath()
        {
            await UploadTextAsync("/a/b/c.txt", "1");
            await UploadTextAsync("/a/x.txt", "2");
            await UploadTextAsync("/a.txt", "3");
            await Shelf.CreateDirectoryAsync("/empty");
            await UploadTextAsync("/a/deleted.txt", "4");
            await UploadTextAsync("/gone/inside.txt", "5");
            await Shelf.DeleteFileAsync("/a/deleted.txt");
            await Shelf.DeleteDirectoryAsync("/gone");

            DirectoryTree tree = await Shelf.ListTreeAsync();

            Assert.Equal(("/", (string?)null), (tree.Directory.Path, tree.NextCursor));
            Assert.Equal(new[] { "/a", "/a.txt", "/a/b", "/a/b/c.txt", "/a/x.txt", "/empty" }, PathsOf(tree));
        }

        [Fact]
        public async Task ListTreeAsync_UnderWithADepth_ListsOnlyThatManyLevelsBelowIt()
        {
            await UploadTextAsync("/top.txt", "0");
            await UploadTextAsync("/a/one.txt", "1");
            await UploadTextAsync("/a/b/two.txt", "2");
            await UploadTextAsync("/a/b/c/three.txt", "3");

            DirectoryTree first = await Shelf.ListTreeAsync(new TreeQuery { Under = "/a", Depth = 1 });
            DirectoryTree second = await Shelf.ListTreeAsync(new TreeQuery { Under = "/a", Depth = 2 });

            Assert.Equal(new[] { "/a/b", "/a/one.txt" }, PathsOf(first));
            Assert.Equal(new[] { "/a/b", "/a/b/c", "/a/b/two.txt", "/a/one.txt" }, PathsOf(second));
        }

        [Fact]
        public async Task ListTreeAsync_Pages_ComeInPathOrderAcrossDirectoriesAndFilesUntilNoCursorIsLeft()
        {
            await UploadTextAsync("/a/1.txt", "1");
            await UploadTextAsync("/a.txt", "2");
            await UploadTextAsync("/b/2.txt", "3");
            await Shelf.CreateDirectoryAsync("/c");
            await UploadTextAsync("/d.txt", "4");

            List<string> paths = new List<string>();
            List<int> pageSizes = new List<int>();
            string? after = null;
            do
            {
                DirectoryTree page = await Shelf.ListTreeAsync(new TreeQuery { Limit = 2, After = after });
                paths.AddRange(PathsOf(page));
                pageSizes.Add(page.Directories.Count + page.Files.Count);
                after = page.NextCursor;
            }
            while (after != null);

            Assert.Equal(new[] { "/a", "/a.txt", "/a/1.txt", "/b", "/b/2.txt", "/c", "/d.txt" }, paths);
            Assert.Equal(new[] { 2, 2, 2, 1 }, pageSizes);
        }

        [Fact]
        public async Task ListTreeAsync_PagesOverNamesBeyondTheBasicPlane_ComeInCodePointOrder()
        {
            // U+FF21 sorts before U+1F600 by code point, which is SQLite's byte order; by UTF-16 units
            // the emoji's high surrogate (U+D83D) would come first. A directory and a file of each, so
            // both the walk's order and the merge of the two kinds are paged over.
            await UploadTextAsync("/\U0001F600/x.txt", "a");
            await UploadTextAsync("/\U0001F600.txt", "b");
            await UploadTextAsync("/Ａ/y.txt", "c");
            await UploadTextAsync("/Ａ.txt", "d");

            List<string> paths = new List<string>();
            string? after = null;
            do
            {
                DirectoryTree page = await Shelf.ListTreeAsync(new TreeQuery { Limit = 1, After = after });
                paths.AddRange(PathsOf(page));
                after = page.NextCursor;
            }
            while (after != null);

            Assert.Equal(new[] { "/Ａ", "/Ａ.txt", "/Ａ/y.txt", "/\U0001F600", "/\U0001F600.txt", "/\U0001F600/x.txt" }, paths);
        }

        [Fact]
        public async Task ListTreeAsync_LimitExactlyTheEntriesLeft_EndsWithoutACursor()
        {
            await UploadTextAsync("/a/1.txt", "1");

            DirectoryTree tree = await Shelf.ListTreeAsync(new TreeQuery { Limit = 2 });

            Assert.Equal((2, (string?)null), (PathsOf(tree).Count, tree.NextCursor));
        }

        [Fact]
        public async Task ListTreeAsync_UnderAFileOrNothing_ThrowsNotFound()
        {
            await UploadTextAsync("/a.txt", "1");

            ByteShelfException file = await RefusalOf(Shelf.ListTreeAsync(new TreeQuery { Under = "/a.txt" }));
            ByteShelfException missing = await RefusalOf(Shelf.ListTreeAsync(new TreeQuery { Under = "/missing" }));

            Assert.Equal((ErrorCodes.NotFound, ErrorCodes.NotFound), (file.Code, missing.Code));
        }

        [Theory]
        [InlineData(0, null)]
        [InlineData(null, 0)]
        [InlineData(null, TreeQuery.MaxLimit + 1)]
        public async Task ListTreeAsync_QueryNotAllowed_ThrowsArgumentException(int? depth, int? limit)
        {
            await Assert.ThrowsAsync<ArgumentException>(() => Shelf.ListTreeAsync(new TreeQuery { Depth = depth, Limit = limit }));
        }

        private static List<string> PathsOf(DirectoryTree tree)
        {
            return tree.InOrder().Select(entry => entry.Path).ToList();
        }
    }
}
