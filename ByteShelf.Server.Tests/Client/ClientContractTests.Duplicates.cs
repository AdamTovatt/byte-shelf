using ByteShelfClient;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.Client
{
    /// <summary>The search for duplicates, the same in both clients.</summary>
    public abstract partial class ClientContractTests
    {
        [Fact]
        public async Task FindDuplicatesAsync_FilesWithTheSameContents_GroupsThemMostRepeatedBytesFirst()
        {
            ShelfFile twoBytes = await UploadTextAsync("/c.txt", "xx");
            await UploadTextAsync("/a.txt", "xx");
            await UploadTextAsync("/b/a.txt", "xx");
            ShelfFile fiveBytes = await UploadTextAsync("/d.txt", "yyyyy");
            await UploadTextAsync("/e.txt", "yyyyy");
            await UploadTextAsync("/single.txt", "z");

            DuplicateSearchResults found = await Shelf.FindDuplicatesAsync(new DuplicateSearch());

            Assert.Equal(
                new[]
                {
                    (fiveBytes.Sha256, 5L, 5L, "/d.txt /e.txt"),
                    (twoBytes.Sha256, 2L, 4L, "/a.txt /b/a.txt /c.txt"),
                },
                found.Groups.Select(group => (group.Sha256, group.SizeBytes, group.RepeatedBytes, string.Join(' ', group.Files.Select(file => file.Path)))));
            Assert.Null(found.NextCursor);
        }

        [Fact]
        public async Task FindDuplicatesAsync_Under_CountsOnlyTheFilesUnderIt()
        {
            await UploadTextAsync("/a/x.txt", "same");
            await UploadTextAsync("/a/deeper/y.txt", "same");
            await UploadTextAsync("/z.txt", "same");
            await UploadTextAsync("/a/one.txt", "other");
            await UploadTextAsync("/two.txt", "other");

            DuplicateSearchResults found = await Shelf.FindDuplicatesAsync(new DuplicateSearch { Under = "/a" });

            DuplicateGroup group = Assert.Single(found.Groups);
            Assert.Equal(new[] { "/a/deeper/y.txt", "/a/x.txt" }, group.Files.Select(file => file.Path));
            Assert.Equal(4, group.RepeatedBytes);
        }

        [Fact]
        public async Task FindDuplicatesAsync_EncryptedFilesWithTheSameBytes_LeavesThemOut()
        {
            using MemoryStream ciphertext = new MemoryStream();
            await (await EncryptedAsync("secret")).CopyToAsync(ciphertext);
            await Shelf.UploadAsync("/a.age", new MemoryStream(ciphertext.ToArray()), new UploadOptions { Encrypted = true });
            await Shelf.UploadAsync("/b.age", new MemoryStream(ciphertext.ToArray()), new UploadOptions { Encrypted = true });
            await UploadTextAsync("/a.txt", "plain");
            await UploadTextAsync("/b.txt", "plain");

            DuplicateGroup group = Assert.Single((await Shelf.FindDuplicatesAsync(new DuplicateSearch())).Groups);
            Assert.Equal(new[] { "/a.txt", "/b.txt" }, group.Files.Select(file => file.Path));
        }

        [Fact]
        public async Task FindDuplicatesAsync_EmptyFiles_LeavesThemOut()
        {
            await UploadTextAsync("/a.txt", string.Empty);
            await UploadTextAsync("/b.txt", string.Empty);
            await UploadTextAsync("/c.txt", "x");
            await UploadTextAsync("/d.txt", "x");

            DuplicateGroup group = Assert.Single((await Shelf.FindDuplicatesAsync(new DuplicateSearch())).Groups);
            Assert.Equal(new[] { "/c.txt", "/d.txt" }, group.Files.Select(file => file.Path));
        }

        [Fact]
        public async Task FindDuplicatesAsync_CopyInTheTrash_LeavesItOut()
        {
            await UploadTextAsync("/a.txt", "same");
            await UploadTextAsync("/b.txt", "same");
            await Shelf.DeleteFileAsync("/b.txt");

            DuplicateSearchResults found = await Shelf.FindDuplicatesAsync(new DuplicateSearch());

            Assert.Empty(found.Groups);
            Assert.Null(found.NextCursor);
        }

        [Fact]
        public async Task FindDuplicatesAsync_Criteria_CountOnlyTheFilesThatMeetThem()
        {
            await UploadTextAsync("/a.png", "picture", contentType: "image/png");
            await UploadTextAsync("/b.png", "picture", contentType: "image/png");
            await UploadTextAsync("/copy.txt", "picture", contentType: "text/plain");
            await UploadTextAsync("/a.txt", "text", contentType: "text/plain");
            await UploadTextAsync("/b.txt", "text", contentType: "text/plain");

            DuplicateSearchResults found = await Shelf.FindDuplicatesAsync(new DuplicateSearch { ContentType = "image/*" });

            DuplicateGroup group = Assert.Single(found.Groups);
            Assert.Equal(new[] { "/a.png", "/b.png" }, group.Files.Select(file => file.Path));
        }

        [Fact]
        public async Task FindDuplicatesAsync_NoDescription_CountsOnlyTheUndescribedCopies()
        {
            await UploadTextAsync("/a.txt", "same");
            await UploadTextAsync("/b.txt", "same");
            await Shelf.UploadAsync("/c.txt", Text("same"), new UploadOptions { Description = "The kept copy" });

            DuplicateSearchResults found = await Shelf.FindDuplicatesAsync(new DuplicateSearch { HasDescription = false });

            DuplicateGroup group = Assert.Single(found.Groups);
            Assert.Equal(new[] { "/a.txt", "/b.txt" }, group.Files.Select(file => file.Path));
        }

        [Fact]
        public async Task FindDuplicatesAsync_Pages_ComeMostRepeatedBytesThenBySha256UntilNoCursorIsLeft()
        {
            ShelfFile largest = await UploadTextAsync("/c1.txt", "ccc");
            await UploadTextAsync("/c2.txt", "ccc");
            List<string> tied = new List<string>();
            foreach (string contents in new[] { "aa", "bb", "dd" })
            {
                tied.Add((await UploadTextAsync($"/{contents}1.txt", contents)).Sha256);
                await UploadTextAsync($"/{contents}2.txt", contents);
            }

            // A page of one, so each page's search sees more groups than it asks for.
            List<string> paged = new List<string>();
            List<DuplicateCursor?> cursors = new List<DuplicateCursor?>();
            DuplicateCursor? after = null;
            do
            {
                DuplicateSearchResults page = await Shelf.FindDuplicatesAsync(new DuplicateSearch { Limit = 1, After = after });
                paged.AddRange(page.Groups.Select(group => group.Sha256));
                after = page.NextCursor;
                cursors.Add(after);
            }
            while (after != null && cursors.Count < 10);

            Assert.Equal(tied.OrderBy(sha256 => sha256, StringComparer.Ordinal).Prepend(largest.Sha256), paged);
            Assert.Equal(4, cursors.Count);
        }

        [Theory]
        [MemberData(nameof(CriteriaNotAllowed))]
        public async Task FindDuplicatesAsync_CriterionNotAllowed_ThrowsArgumentException(FileCriteria criteria)
        {
            await Assert.ThrowsAsync<ArgumentException>(() => Shelf.FindDuplicatesAsync(new DuplicateSearch(criteria)));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(DuplicateSearch.MaxLimit + 1)]
        public async Task FindDuplicatesAsync_LimitOutOfRange_ThrowsArgumentException(int limit)
        {
            await Assert.ThrowsAsync<ArgumentException>(() => Shelf.FindDuplicatesAsync(new DuplicateSearch { Limit = limit }));
        }

        [Fact]
        public async Task FindDuplicatesAsync_UnderMissing_ThrowsNotFound()
        {
            ByteShelfException exception = await RefusalOf(Shelf.FindDuplicatesAsync(new DuplicateSearch { Under = "/missing" }));

            Assert.Equal(ErrorCodes.NotFound, exception.Code);
        }
    }
}
