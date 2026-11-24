using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfClient;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.Client
{
    /// <summary>
    /// What plugins keep about files, in both clients: each has <see cref="TestMetadataPlugins.Declaration"/>
    /// installed. Documents about contents are the server's alone, and tested there.
    /// </summary>
    public abstract partial class ClientContractTests
    {
        private const string Plugin = TestPluginMetadata.Plugin;

        [Fact]
        public async Task SetPluginDocumentAsync_FirstWriteAndTheNext_CountVersionsAndAreReadBack()
        {
            ShelfFile file = await UploadTextAsync("/a.txt", "a");

            PluginFileDetails first = await SetAsync("/a.txt", "{\"rating\":4,\"other\":[1]}", null);
            PluginFileDetails second = await SetAsync("/a.txt", "{\"rating\":5}", 1);

            Assert.Equal((1, "{\"rating\":4,\"other\":[1]}"), (first.FileVersion, first.FileDocument?.Json));
            Assert.Equal((2, "{\"rating\":5}"), (second.FileVersion, second.FileDocument?.Json));
            PluginFileDetails read = (await Shelf.GetFileMetadataAsync("/a.txt")).Plugins[Plugin];
            Assert.Equal(("Test plugin", 2, "{\"rating\":5}"), (read.Name, read.FileVersion, read.FileDocument?.Json));
            Assert.Null(read.ContentsDocument);
            Assert.Equal(file.UpdatedAt, (await Shelf.GetFileAsync("/a.txt")).UpdatedAt);
        }

        [Theory]
        [InlineData(null)]
        [InlineData(2)]
        public async Task SetPluginDocumentAsync_AnotherVersionIsThere_IsAConflict(int? expectedVersion)
        {
            await UploadTextAsync("/a.txt", "a");
            await SetAsync("/a.txt", "{\"rating\":4}", null);

            ByteShelfException refusal = await RefusalOf(SetAsync("/a.txt", "{}", expectedVersion));

            Assert.Equal(ErrorCodes.Conflict, refusal.Code);
            Assert.Equal("{\"rating\":4}", (await Shelf.GetFileMetadataAsync("/a.txt")).Plugins[Plugin].FileDocument?.Json);
        }

        [Fact]
        public async Task SetPluginDocumentAsync_Null_RemovesItAndItsVersionGoesOnCounting()
        {
            await UploadTextAsync("/a.txt", "a");
            await SetAsync("/a.txt", "{\"rating\":4}", null);

            await SetAsync("/a.txt", null, 1);

            PluginFileDetails removed = (await Shelf.GetFileMetadataAsync("/a.txt")).Plugins[Plugin];
            Assert.Equal((2, null), (removed.FileVersion, removed.FileDocument));
            Assert.Empty(removed.Fields);
            Assert.Equal(ErrorCodes.Conflict, (await RefusalOf(SetAsync("/a.txt", "{}", null))).Code);
            Assert.Equal(3, (await SetAsync("/a.txt", "{}", 2)).FileVersion);
        }

        [Fact]
        public async Task SetPluginDocumentAsync_NullWhenNoneWasWritten_ChangesNothing()
        {
            await UploadTextAsync("/a.txt", "a");

            PluginFileDetails kept = await SetAsync("/a.txt", null, null);

            Assert.Equal((null, null), (kept.FileVersion, kept.FileDocument));
            Assert.Empty((await Shelf.GetFileMetadataAsync("/a.txt")).Plugins);
        }

        [Fact]
        public async Task SetPluginDocumentAsync_PluginThatIsNotInstalled_IsNotFound()
        {
            await UploadTextAsync("/a.txt", "a");

            ByteShelfException refusal = await RefusalOf(Shelf.SetPluginDocumentAsync("/a.txt", "never-installed", PluginDocument.Parse("{}"), null));

            Assert.Equal(ErrorCodes.NotFound, refusal.Code);
        }

        [Fact]
        public async Task SetPluginDocumentAsync_MissingFile_IsNotFound()
        {
            Assert.Equal(ErrorCodes.NotFound, (await RefusalOf(SetAsync("/missing.txt", "{}", null))).Code);
        }

        [Fact]
        public async Task SetPluginDocumentAsync_OnAReplica_IsRefused()
        {
            IByteShelfClient follower = await FollowAsync();

            ByteShelfException refusal = await RefusalOf(follower.Shelf("docs").SetPluginDocumentAsync("/a.txt", Plugin, PluginDocument.Parse("{}"), null));

            Assert.Equal(ErrorCodes.ReadOnlyReplica, refusal.Code);
        }

        [Fact]
        public async Task GetFileMetadataAsync_DeclaredFields_AreThoseWithAValueOfTheirKind()
        {
            await UploadTextAsync("/a.txt", "a");
            await SetAsync("/a.txt", "{\"due\":\"2026-03-01T14:30:00+02:00\",\"rating\":\"five\",\"done\":false,\"note\":\"x\",\"undeclared\":1}", null);

            PluginFileDetails kept = (await Shelf.GetFileMetadataAsync("/a.txt")).Plugins[Plugin];

            Assert.Equal(["note", "due", "done"], kept.Fields.Select(field => field.Name));
            PluginFieldEntry due = kept.Fields[1];
            Assert.Equal(("Due", PluginFieldKind.Date, PluginDocumentSubject.File), (due.Label, due.Kind, due.Document));
            Assert.Equal(PluginFieldValue.OfDate(new DateTimeOffset(2026, 3, 1, 12, 30, 0, TimeSpan.Zero)), due.Value);
        }

        [Fact]
        public async Task UploadAsync_OverAFile_KeepsWhatPluginsKeepAboutIt()
        {
            await UploadTextAsync("/a.txt", "a");
            await SetAsync("/a.txt", "{\"rating\":4}", null);

            await UploadTextAsync("/a.txt", "b", overwrite: true);

            PluginFileDetails kept = (await Shelf.GetFileMetadataAsync("/a.txt")).Plugins[Plugin];
            Assert.Equal((1, "{\"rating\":4}"), (kept.FileVersion, kept.FileDocument?.Json));
            Assert.Equal(["/a.txt"], await FindAsync($"{Plugin}.rating=4"));
        }

        [Fact]
        public async Task UploadAsync_OverAFile_LeavesTheOldVersionItsOwnDocument()
        {
            await UploadTextAsync("/a.txt", "a");
            await SetAsync("/a.txt", "{\"rating\":4}", null);
            await UploadTextAsync("/a.txt", "b", overwrite: true);
            TrashEntry old = Assert.Single(await Shelf.GetTrashAsync());

            await SetAsync("/a.txt", "{\"rating\":5}", 1);
            await Shelf.MoveFileAsync("/a.txt", "/new.txt");
            await Shelf.RestoreAsync(old.DeletionId);

            Assert.Equal(["/a.txt"], await FindAsync($"{Plugin}.rating=4"));
            Assert.Equal(["/new.txt"], await FindAsync($"{Plugin}.rating=5"));
        }

        [Fact]
        public async Task SwapContentAsync_FileEncrypted_KeepsWhatPluginsKeepAboutIt()
        {
            ShelfFile file = await UploadTextAsync("/a.txt", "a");
            await SetAsync("/a.txt", "{\"rating\":4}", null);

            await Shelf.SwapContentAsync(file, new MemoryStream("age-encryption.org/v1\nx"u8.ToArray()), encrypted: true);

            Assert.Equal("{\"rating\":4}", (await Shelf.GetFileMetadataAsync("/a.txt")).Plugins[Plugin].FileDocument?.Json);
            Assert.Equal(["/a.txt"], await FindAsync($"{Plugin}.rating=4"));
        }

        [Fact]
        public async Task Listings_FieldsDeclaredForThem_AreCarriedByEveryListingAndNothingElse()
        {
            ShelfFile file = await UploadTextAsync("/d/a.txt", "a");
            await UploadTextAsync("/d/b.txt", "b");
            string longCaption = new string('å', 128);
            await SetAsync("/d/a.txt", $"{{\"caption\":\"{longCaption}\",\"rating\":4,\"note\":\"not listed\",\"done\":true,\"due\":\"2026-03-01T00:00:00Z\"}}", null);

            Assert.Null((await Shelf.GetFileAsync("/d/a.txt")).PluginFields);
            AssertListed((await Shelf.ListAsync("/d")).Files);
            AssertListed((await Shelf.ListTreeAsync()).Files);
            AssertListed((await Shelf.SearchAsync(new FileSearch())).Files);
            Assert.Null((await Shelf.MoveFileAsync("/d/b.txt", "/d/c.txt")).PluginFields);
            TrashEntry deleted = await Shelf.DeleteDirectoryAsync("/d");
            AssertListed((await Shelf.ListDeletionAsync(deleted.DeletionId)).Tree.Files);

            void AssertListed(IReadOnlyList<ShelfFile> files)
            {
                Assert.Equal(2, files.Count);
                Assert.Null(files[1].PluginFields);
                (string plugin, IReadOnlyDictionary<string, PluginFieldValue> values) = Assert.Single(files[0].PluginFields!);
                Assert.Equal((file.Id, Plugin), (files[0].Id, plugin));
                Assert.Equal(["caption", "done", "due", "rating"], values.Keys.Order(StringComparer.Ordinal));
                Assert.Equal((longCaption, 4d, true), (values["caption"].Text, values["rating"].Number, values["done"].Flag));
                Assert.Equal(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero), values["due"].Date);
            }
        }

        [Fact]
        public async Task ListAsync_TextOfMoreThan256Bytes_IsLeftOut()
        {
            await UploadTextAsync("/a.txt", "a");
            await SetAsync("/a.txt", $"{{\"caption\":\"{new string('å', 128)}x\"}}", null);

            Assert.Null(Assert.Single((await Shelf.ListAsync()).Files).PluginFields);
        }

        [Theory]
        [InlineData("test-plugin.rating", "a b c")]
        [InlineData("!test-plugin.rating", "d e")]
        [InlineData("test-plugin.rating=2..3", "b c")]
        [InlineData("test-plugin.rating=2.5..", "c")]
        [InlineData("test-plugin.rating=..1", "a")]
        [InlineData("test-plugin.rating=-5", "")]
        [InlineData("test-plugin.caption~ÄPPLE", "a b")]
        [InlineData("test-plugin.caption~ett ä", "a")]
        [InlineData("test-plugin.due=2026-01-01..2026-01-03", "a b")]
        [InlineData("test-plugin.due=2026-01-02T01:00:00+01:00..", "b c")]
        [InlineData("test-plugin.due=..2026-01-02", "a")]
        [InlineData("test-plugin.done=true", "a")]
        [InlineData("test-plugin.done=false", "b")]
        [InlineData("!test-plugin.done", "c d e")]
        public async Task SearchAsync_Filter_FindsTheFilesWhoseFieldMeetsIt(string filter, string found)
        {
            await UploadTextAsync("/a", "a");
            await UploadTextAsync("/b", "b");
            await UploadTextAsync("/c", "c");
            await UploadTextAsync("/d", "d");
            await UploadTextAsync("/e", "e");
            await SetAsync("/a", "{\"rating\":1,\"caption\":\"Ett äpple\",\"due\":\"2026-01-01T00:00:00Z\",\"done\":true}", null);
            await SetAsync("/b", "{\"rating\":2,\"caption\":\"två ÄPPLEN\",\"due\":\"2026-01-02T00:00:00Z\",\"done\":false}", null);
            await SetAsync("/c", "{\"rating\":3,\"due\":\"2026-01-03T00:00:00Z\",\"done\":\"true\"}", null);
            await SetAsync("/d", "{\"rating\":\"4\",\"caption\":5}", null);

            Assert.Equal(found, string.Join(' ', (await FindAsync(filter)).Select(path => path[1..])));
        }

        [Fact]
        public async Task SearchAsync_FieldOfTheContentsWrittenAboutTheFile_HasNoValue()
        {
            await UploadTextAsync("/a.txt", "a");
            await SetAsync("/a.txt", "{\"width\":10,\"words\":\"apple\"}", null);

            Assert.Empty(await FindAsync($"{Plugin}.width"));
            Assert.Null(Assert.Single((await Shelf.ListAsync()).Files).PluginFields);
        }

        [Fact]
        public async Task SearchAsync_SeveralFilters_MustAllBeMetWithTheOtherCriteria()
        {
            await UploadTextAsync("/a.txt", "a");
            await UploadTextAsync("/b.txt", "b");
            await UploadTextAsync("/d/a.txt", "c");
            foreach (string path in new[] { "/a.txt", "/b.txt", "/d/a.txt" })
            {
                await SetAsync(path, "{\"rating\":4,\"done\":true}", null);
            }

            await SetAsync("/b.txt", "{\"rating\":4}", 1);

            FileSearchResults found = await Shelf.SearchAsync(new FileSearch
            {
                Text = "a.txt",
                Under = "/d",
                PluginFields = TestMetadataPlugins.Filters($"{Plugin}.rating=4", $"{Plugin}.done=true"),
            });
            Assert.Equal(["/d/a.txt"], found.Files.Select(file => file.Path));
            Assert.Equal(["/a.txt", "/d/a.txt"], await FindAsync($"{Plugin}.rating=4", $"{Plugin}.done=true"));
        }

        [Fact]
        public async Task SearchAsync_Text_MatchesSearchedFieldsAlone()
        {
            await UploadTextAsync("/a.txt", "a");
            await UploadTextAsync("/b.txt", "b");
            await SetAsync("/a.txt", "{\"caption\":\"Ett ÄPPLE\"}", null);
            await SetAsync("/b.txt", "{\"note\":\"Ett ÄPPLE\"}", null);

            FileSearchResults found = await Shelf.SearchAsync(new FileSearch { Text = "äpple" });

            Assert.Equal(["/a.txt"], found.Files.Select(file => file.Path));
        }

        [Fact]
        public async Task SearchAsync_DocumentWrittenAnewOrRemoved_IsFoundByItsNewValuesAlone()
        {
            await UploadTextAsync("/a.txt", "a");
            await SetAsync("/a.txt", "{\"caption\":\"apple\",\"rating\":4}", null);

            await SetAsync("/a.txt", "{\"rating\":5}", 1);
            Assert.Empty((await Shelf.SearchAsync(new FileSearch { Text = "apple" })).Files);
            Assert.Equal(["/a.txt"], await FindAsync($"{Plugin}.rating=5"));

            await SetAsync("/a.txt", null, 2);
            Assert.Empty(await FindAsync($"{Plugin}.rating"));
        }

        [Fact]
        public async Task SearchAsync_TextBeyondTheFirst1000CodePointsOfAField_IsNotMatched()
        {
            await UploadTextAsync("/a.txt", "a");
            await SetAsync("/a.txt", $"{{\"caption\":\"{new string('x', 999)}yz\"}}", null);

            Assert.Equal(["/a.txt"], await FindAsync($"{Plugin}.caption~xy"));
            Assert.Empty(await FindAsync($"{Plugin}.caption~yz"));
        }

        [Theory]
        [InlineData("never-installed.rating", "There is no plugin 'never-installed' here")]
        [InlineData("test-plugin.never-declared", "declares no field 'never-declared'")]
        [InlineData("test-plugin.rating~x", "is a number, which cannot be compared as a text")]
        [InlineData("test-plugin.caption=1", "is a text, which cannot be compared as a number")]
        public async Task SearchAsync_FilterOnWhatIsNotDeclared_IsRefused(string filter, string why)
        {
            ByteShelfException search = await RefusalOf(FindAsync(filter));
            ByteShelfException duplicates = await RefusalOf(Shelf.FindDuplicatesAsync(new DuplicateSearch { PluginFields = TestMetadataPlugins.Filters(filter) }));

            Assert.Equal((ErrorCodes.InvalidRequest, ErrorCodes.InvalidRequest), (search.Code, duplicates.Code));
            Assert.Contains(why, search.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task SearchAsync_ContentsFieldOfAFileNotRead_HasNoValue()
        {
            await UploadTextAsync("/a.txt", "a");

            Assert.Empty(await FindAsync($"{Plugin}.width"));
            Assert.Equal(["/a.txt"], await FindAsync($"!{Plugin}.width"));
        }

        [Fact]
        public async Task FindDuplicatesAsync_Filter_TakesOnlyTheFilesThatMeetIt()
        {
            foreach (string path in new[] { "/a.txt", "/b.txt", "/c.txt" })
            {
                await UploadTextAsync(path, "same");
            }

            await SetAsync("/a.txt", "{\"rating\":4}", null);
            await SetAsync("/c.txt", "{\"rating\":4}", null);

            DuplicateSearchResults found = await Shelf.FindDuplicatesAsync(new DuplicateSearch { PluginFields = TestMetadataPlugins.Filters($"{Plugin}.rating=4") });

            Assert.Equal(["/a.txt", "/c.txt"], Assert.Single(found.Groups).Files.Select(file => file.Path));
        }

        private Task<PluginFileDetails> SetAsync(string path, string? json, int? expectedVersion)
        {
            return Shelf.SetPluginDocumentAsync(path, Plugin, json == null ? null : PluginDocument.Parse(json), expectedVersion);
        }

        private async Task<IEnumerable<string>> FindAsync(params string[] filters)
        {
            FileSearchResults found = await Shelf.SearchAsync(new FileSearch { PluginFields = TestMetadataPlugins.Filters(filters) });
            return found.Files.Select(file => file.Path);
        }
    }
}
