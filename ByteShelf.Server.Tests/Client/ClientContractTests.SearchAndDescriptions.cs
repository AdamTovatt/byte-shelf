using System.Text;
using ByteShelfClient;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.Client
{
    /// <summary>Descriptions and search, the same in both clients.</summary>
    public abstract partial class ClientContractTests
    {
        [Fact]
        public async Task UploadAsync_WithDescription_StoresItTrimmed()
        {
            ShelfFile stored = await Shelf.UploadAsync("/a.txt", Text("a"), new UploadOptions { Description = "  The March invoice  " });

            Assert.Equal("The March invoice", stored.Description);
            Assert.Equal("The March invoice", (await Shelf.GetFileAsync("/a.txt")).Description);
        }

        [Fact]
        public async Task UploadAsync_LongFileWithDescription_StoresIt()
        {
            ShelfFile stored = await Shelf.UploadAsync("/long.txt", Text(LongText), new UploadOptions { Description = "long" });

            Assert.Equal(("long", LongText), (stored.Description, await ReadTextAsync("/long.txt")));
        }

        [Fact]
        public async Task SetDescriptionAsync_Text_SetsIt()
        {
            await UploadTextAsync("/a.txt", "a");

            ShelfFile described = await Shelf.SetDescriptionAsync("/a.txt", "Signed copy");

            Assert.Equal("Signed copy", described.Description);
            Assert.Equal("Signed copy", (await Shelf.GetFileAsync("/a.txt")).Description);
        }

        [Fact]
        public async Task SetDescriptionAsync_Blank_RemovesIt()
        {
            ShelfFile stored = await Shelf.UploadAsync("/a.txt", Text("a"), new UploadOptions { Description = "Signed copy" });

            ShelfFile cleared = await Shelf.SetDescriptionAsync(stored.Id, "   ");

            Assert.Null(cleared.Description);
            Assert.Null((await Shelf.GetFileAsync("/a.txt")).Description);
        }

        [Fact]
        public async Task UploadAsync_OverwriteWithoutADescription_KeepsTheReplacedFilesOne()
        {
            await Shelf.UploadAsync("/a.txt", Text("old"), new UploadOptions { Description = "The signed copy" });

            ShelfFile replaced = await UploadTextAsync("/a.txt", "new", overwrite: true);

            Assert.Equal("The signed copy", replaced.Description);
        }

        [Fact]
        public async Task UploadAsync_OverwriteWithADescription_StoresTheNewOne()
        {
            await Shelf.UploadAsync("/a.txt", Text("old"), new UploadOptions { Description = "Draft" });

            ShelfFile replaced = await Shelf.UploadAsync("/a.txt", Text("new"), new UploadOptions { Overwrite = true, Description = "Final" });

            Assert.Equal("Final", replaced.Description);
        }

        [Fact]
        public async Task SetDescriptionAsync_AtTheLimitInCharacters_IsKept()
        {
            await UploadTextAsync("/a.txt", "a");

            // Two UTF-16 units each, one character each: the limit counts characters.
            string longest = string.Concat(Enumerable.Repeat("😀", FileDescription.MaxLength));

            Assert.Equal(longest, (await Shelf.SetDescriptionAsync("/a.txt", longest)).Description);
        }

        [Fact]
        public async Task SetDescriptionAsync_OverTheLimit_ThrowsArgumentException()
        {
            await UploadTextAsync("/a.txt", "a");

            await Assert.ThrowsAsync<ArgumentException>(() => Shelf.SetDescriptionAsync("/a.txt", new string('a', FileDescription.MaxLength + 1)));
        }

        [Fact]
        public async Task SetDescriptionAsync_FileInTheTrash_ThrowsNotFound()
        {
            await UploadTextAsync("/a.txt", "a");
            await Shelf.DeleteFileAsync("/a.txt");

            ByteShelfException exception = await RefusalOf(Shelf.SetDescriptionAsync("/a.txt", "x"));

            Assert.Equal(ErrorCodes.NotFound, exception.Code);
        }

        [Fact]
        public async Task SearchAsync_Text_MatchesNameOrDescriptionIgnoringCaseBeyondAscii()
        {
            await UploadTextAsync("/Årsrapport.pdf", "a");
            await Shelf.UploadAsync("/scan-17.pdf", Text("b"), new UploadOptions { Description = "Kvitto från ÅHLÉNS" });
            await UploadTextAsync("/other.pdf", "c");

            Assert.Equal(new[] { "/Årsrapport.pdf" }, await SearchPathsAsync(new FileSearch { Text = "årsRAPP" }));
            Assert.Equal(new[] { "/scan-17.pdf" }, await SearchPathsAsync(new FileSearch { Text = "åhléns" }));
        }

        [Fact]
        public async Task SearchAsync_TextDecomposed_MatchesTheComposedName()
        {
            await UploadTextAsync("/Årsrapport.pdf", "a");

            // "a" followed by a combining ring: the same letter as "å", which the stored name holds composed.
            Assert.Equal(new[] { "/Årsrapport.pdf" }, await SearchPathsAsync(new FileSearch { Text = "årsrapp" }));
        }

        [Fact]
        public async Task SearchAsync_LowercaseText_MatchesAnUppercaseNonAsciiLetter()
        {
            await UploadTextAsync("/ÖVERSIKT.txt", "a");
            await UploadTextAsync("/other.txt", "b");

            Assert.Equal(new[] { "/ÖVERSIKT.txt" }, await SearchPathsAsync(new FileSearch { Text = "ö" }));
        }

        [Fact]
        public async Task SearchAsync_AfterARenameAndANewDescription_MatchesOnlyTheNewText()
        {
            await Shelf.UploadAsync("/Öl.txt", Text("a"), new UploadOptions { Description = "Gammal Ö" });
            await Shelf.MoveFileAsync("/Öl.txt", "/Äpple.txt");
            await Shelf.SetDescriptionAsync("/Äpple.txt", "Ny Ü");

            Assert.Empty(await SearchPathsAsync(new FileSearch { Text = "ö" }));
            Assert.Equal(new[] { "/Äpple.txt" }, await SearchPathsAsync(new FileSearch { Text = "äpp" }));
            Assert.Equal(new[] { "/Äpple.txt" }, await SearchPathsAsync(new FileSearch { Text = "ny ü" }));
        }

        [Fact]
        public async Task SearchAsync_Under_FindsOnlyLiveFilesBelowIt()
        {
            await UploadTextAsync("/a/one.txt", "1");
            await UploadTextAsync("/a/b/two.txt", "2");
            await UploadTextAsync("/a/c/gone.txt", "3");
            await UploadTextAsync("/a/deleted.txt", "4");
            await UploadTextAsync("/elsewhere.txt", "5");
            await Shelf.DeleteDirectoryAsync("/a/c");
            await Shelf.DeleteFileAsync("/a/deleted.txt");

            Assert.Equal(new[] { "/a/b/two.txt", "/a/one.txt" }, await SearchPathsAsync(new FileSearch { Under = "/a" }));
        }

        [Fact]
        public async Task SearchAsync_UnderMissingDirectory_ThrowsNotFound()
        {
            ByteShelfException exception = await RefusalOf(Shelf.SearchAsync(new FileSearch { Under = "/missing" }));

            Assert.Equal(ErrorCodes.NotFound, exception.Code);
        }

        [Fact]
        public async Task SearchAsync_ContentType_IgnoresParametersAndCaseAndTakesAWildcardSubtype()
        {
            await UploadTextAsync("/a.txt", "a", contentType: "Text/Plain; charset=utf-8");
            await UploadTextAsync("/b.png", "b", contentType: "image/png");
            await UploadTextAsync("/c.jpg", "c", contentType: "image/jpeg");

            Assert.Equal(new[] { "/a.txt" }, await SearchPathsAsync(new FileSearch { ContentType = "text/plain" }));
            Assert.Equal(new[] { "/b.png", "/c.jpg" }, await SearchPathsAsync(new FileSearch { ContentType = "IMAGE/*" }));
        }

        [Fact]
        public async Task SearchAsync_SizeRangeAndSha256_NarrowTheFiles()
        {
            ShelfFile small = await UploadTextAsync("/small.txt", "a");
            await UploadTextAsync("/middle.txt", "abc");
            await UploadTextAsync("/large.txt", "abcdefgh");

            Assert.Equal(new[] { "/middle.txt", "/small.txt" }, await SearchPathsAsync(new FileSearch { MinSizeBytes = 1, MaxSizeBytes = 3 }));
            Assert.Equal(new[] { "/small.txt" }, await SearchPathsAsync(new FileSearch { Sha256 = small.Sha256.ToUpperInvariant() }));
        }

        [Fact]
        public async Task SearchAsync_TimeRanges_IncludeTheirStartAndExcludeTheirEnd()
        {
            ShelfFile stored = await UploadTextAsync("/a.txt", "a");

            Assert.Equal(new[] { "/a.txt" }, await SearchPathsAsync(new FileSearch { CreatedFrom = stored.CreatedAt, UpdatedFrom = stored.UpdatedAt }));
            Assert.Equal(new[] { "/a.txt" }, await SearchPathsAsync(new FileSearch { CreatedBefore = stored.CreatedAt.AddTicks(1), UpdatedBefore = stored.UpdatedAt.AddTicks(1) }));
            Assert.Empty(await SearchPathsAsync(new FileSearch { CreatedFrom = stored.CreatedAt.AddTicks(1) }));
            Assert.Empty(await SearchPathsAsync(new FileSearch { CreatedBefore = stored.CreatedAt }));
            Assert.Empty(await SearchPathsAsync(new FileSearch { UpdatedFrom = stored.UpdatedAt.AddTicks(1) }));
            Assert.Empty(await SearchPathsAsync(new FileSearch { UpdatedBefore = stored.UpdatedAt }));
        }

        [Fact]
        public async Task SearchAsync_Pages_ComeInPathOrderUntilNoCursorIsLeft()
        {
            await UploadTextAsync("/c.txt", "c");
            await UploadTextAsync("/a.txt", "a");
            await UploadTextAsync("/b/x.txt", "b");

            FileSearchResults first = await Shelf.SearchAsync(new FileSearch { Limit = 2 });
            FileSearchResults second = await Shelf.SearchAsync(new FileSearch { Limit = 2, After = first.NextCursor });

            Assert.Equal(new[] { "/a.txt", "/b/x.txt" }, first.Files.Select(file => file.Path));
            Assert.NotNull(first.NextCursor);
            Assert.Equal(new[] { "/c.txt" }, second.Files.Select(file => file.Path));
            Assert.Null(second.NextCursor);
        }

        [Fact]
        public async Task SearchAndListAsync_NamesBeyondTheBasicPlane_ComeInCodePointOrder()
        {
            // U+FF21 sorts before U+1F600 by code point, which is SQLite's byte order; by UTF-16 units
            // the emoji's high surrogate (U+D83D) would come first.
            await UploadTextAsync("/\U0001F600.txt", "a");
            await UploadTextAsync("/Ａ.txt", "b");

            string[] expected = { "/Ａ.txt", "/\U0001F600.txt" };
            Assert.Equal(expected, await SearchPathsAsync(new FileSearch()));
            Assert.Equal(expected, (await Shelf.ListAsync()).Files.Select(file => file.Path));
        }

        [Fact]
        public async Task SearchAsync_LimitExactlyTheFilesLeft_EndsWithoutACursor()
        {
            await UploadTextAsync("/a.txt", "a");
            await UploadTextAsync("/b.txt", "b");

            FileSearchResults page = await Shelf.SearchAsync(new FileSearch { Limit = 2 });

            Assert.Equal(2, page.Files.Count);
            Assert.Null(page.NextCursor);
        }

        public static TheoryData<FileSearch> SearchesNotAllowed => new TheoryData<FileSearch>
        {
            new FileSearch { ContentType = "image/png/*" },
            new FileSearch { Sha256 = "abc" },
            new FileSearch { Limit = FileSearch.MaxLimit + 1 },
            new FileSearch { Limit = 0 },
            new FileSearch { MinSizeBytes = -1 },
            new FileSearch { MinSizeBytes = 5, MaxSizeBytes = 4 },
            new FileSearch { CreatedFrom = DateTimeOffset.UnixEpoch, CreatedBefore = DateTimeOffset.UnixEpoch },
            new FileSearch { UpdatedFrom = DateTimeOffset.UnixEpoch, UpdatedBefore = DateTimeOffset.UnixEpoch },
        };

        [Theory]
        [MemberData(nameof(SearchesNotAllowed))]
        public async Task SearchAsync_CriterionNotAllowed_ThrowsArgumentException(FileSearch search)
        {
            await Assert.ThrowsAsync<ArgumentException>(() => Shelf.SearchAsync(search));
        }

        private async Task<string[]> SearchPathsAsync(FileSearch search)
        {
            return (await Shelf.SearchAsync(search)).Files.Select(file => file.Path).ToArray();
        }

        private static MemoryStream Text(string text)
        {
            return new MemoryStream(Encoding.UTF8.GetBytes(text));
        }
    }
}
