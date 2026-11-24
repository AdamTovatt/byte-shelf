using System.Text;
using ByteShelf.Server.Metadata.Services;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;
using Microsoft.Extensions.DependencyInjection;

namespace ByteShelf.Server.Tests.Metadata
{
    /// <summary>
    /// Finding and listing files by the fields a plugin declares in the document about their contents, which
    /// the server alone has. What both clients share, for the document about a file, is in the client contract tests.
    /// </summary>
    public class PluginFieldSearchTests : ServiceTestBase
    {
        private const string Plugin = TestPluginMetadata.Plugin;

        private static readonly TestMetadataPlugin TestPlugin = new TestMetadataPlugin(TestMetadataPlugins.Declaration);

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            InstallPlugins(TestPlugin);
        }

        [Fact]
        public async Task SearchAsync_FieldOfTheContents_FindsEveryPlainFileThatHoldsThem()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile first = await StoreTextAsync(shelf, "/a.txt", "same");
            await StoreTextAsync(shelf, "/b.txt", "same");
            await StoreTextAsync(shelf, "/c.txt", "other");
            await Services.RecordContentDocumentAsync(first.Sha256, "{\"width\":10}");

            Assert.Equal(["/a.txt", "/b.txt"], await FindAsync(shelf, $"{Plugin}.width=10"));
            Assert.Equal(["/c.txt"], await FindAsync(shelf, $"!{Plugin}.width"));
        }

        [Fact]
        public async Task SearchAsync_TextInASearchedFieldOfTheContents_FindsTheFile()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");
            await Services.RecordContentDocumentAsync(file.Sha256, "{\"words\":\"Ett ÄPPLE\"}");

            FileSearchResults found = await FileService.SearchAsync(shelf, new FileSearch { Text = "äpple" });

            Assert.Equal("/a.txt", Assert.Single(found.Files).Path);
        }

        [Fact]
        public async Task SearchAsync_EncryptedFileWithTheBytesOfAPlainOne_HasNoFieldOfTheContents()
        {
            Shelf shelf = await CreateShelfAsync();
            byte[] bytes = Encoding.ASCII.GetBytes("age-encryption.org/v1\nstored as plain as well");
            ShelfFile plain = await StoreAsync(shelf, "/plain.txt", bytes, "text/plain");
            await StoreAsync(shelf, "/secret.txt", bytes, "text/plain", encrypted: true);
            await Services.RecordContentDocumentAsync(plain.Sha256, "{\"words\":\"apple\",\"width\":10}");

            Assert.Equal(["/plain.txt"], await FindAsync(shelf, $"{Plugin}.width"));
            Assert.Equal(["/secret.txt"], await FindAsync(shelf, $"!{Plugin}.width"));
            FileSearchResults byText = await FileService.SearchAsync(shelf, new FileSearch { Text = "apple" });
            Assert.Equal("/plain.txt", Assert.Single(byText.Files).Path);
            DirectoryListing listing = await DirectoryService.ListAsync(shelf, EntryReference.ForPath("/"));
            Assert.Equal([true, false], listing.Files.Select(file => file.PluginFields != null));
        }

        [Fact]
        public async Task SearchAsync_AnotherPluginHasAFieldOfTheSameName_FindsByTheOneNamed()
        {
            PluginField rating = new PluginField("rating", "Rating", PluginFieldKind.Number, PluginDocumentSubject.File);
            InstallPlugins(TestPlugin, new TestMetadataPlugin(new PluginDeclaration("other-plugin", "Other plugin", [rating])));
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");
            await Services.SetFileDocumentAsync(file.Id, "{\"rating\":4}");

            Assert.Equal(["/a.txt"], await FindAsync(shelf, $"{Plugin}.rating=4"));
            Assert.Empty(await FindAsync(shelf, "other-plugin.rating=4"));
        }

        [Fact]
        public async Task SearchAsync_FieldDeclaredInOneDocumentWrittenInTheOther_IsNotTaken()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");
            await Services.RecordContentDocumentAsync(file.Sha256, "{\"rating\":4}");
            await Services.SetFileDocumentAsync(file.Id, "{\"width\":10}");

            Assert.Empty(await FindAsync(shelf, $"{Plugin}.rating"));
            Assert.Empty(await FindAsync(shelf, $"{Plugin}.width"));
        }

        [Fact]
        public async Task SearchAsync_FileEncryptedInPlace_LosesTheContentsFieldsAndKeepsItsOwn()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");
            await Services.RecordContentDocumentAsync(file.Sha256, "{\"width\":10}");
            await Services.SetFileDocumentAsync(file.Id, "{\"rating\":4}");

            await EncryptInPlaceAsync(shelf, file);

            Assert.Empty(await FindAsync(shelf, $"{Plugin}.width"));
            Assert.Equal(["/a.txt"], await FindAsync(shelf, $"{Plugin}.rating=4"));
        }

        [Fact]
        public async Task SearchAsync_ContentsReadAgain_HasTheNewValuesAlone()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");
            await Services.RecordContentDocumentAsync(file.Sha256, "{\"width\":10,\"words\":\"apple\"}");

            await Services.RecordContentDocumentAsync(file.Sha256, "{\"width\":20}", version: 2);

            Assert.Equal(["/a.txt"], await FindAsync(shelf, $"{Plugin}.width=20"));
            Assert.Empty(await FindAsync(shelf, $"{Plugin}.words"));
        }

        [Fact]
        public async Task FindDuplicatesAsync_Filter_TakesOnlyTheFilesThatMeetIt()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile first = await StoreTextAsync(shelf, "/a.txt", "same");
            await StoreTextAsync(shelf, "/b.txt", "same");
            ShelfFile other = await StoreTextAsync(shelf, "/c.txt", "others");
            await StoreTextAsync(shelf, "/d.txt", "others");
            await Services.RecordContentDocumentAsync(first.Sha256, "{\"width\":10}");
            await Services.RecordContentDocumentAsync(other.Sha256, "{\"width\":20}");

            DuplicateSearchResults found = await FileService.FindDuplicatesAsync(shelf, new DuplicateSearch { PluginFields = TestMetadataPlugins.Filters($"{Plugin}.width=10") });

            Assert.Equal(first.Sha256, Assert.Single(found.Groups).Sha256);
        }

        [Fact]
        public async Task GetAsync_PluginNoLongerInstalled_ShowsFindsAndListsNothingOfItUntilItIsBack()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");
            await Services.SetFileDocumentAsync(file.Id, "{\"caption\":\"apple\"}");
            await Services.StartPluginFieldIndexAsync();

            InstallPlugins();
            await Services.StartPluginFieldIndexAsync();
            Assert.Empty((await MetadataAsync(shelf, file)).Plugins);
            Assert.Empty((await FileService.SearchAsync(shelf, new FileSearch { Text = "apple" })).Files);
            Assert.Null(Assert.Single((await DirectoryService.ListAsync(shelf, EntryReference.ForPath("/"))).Files).PluginFields);
            await Assert.ThrowsAsync<ArgumentException>(() => FindAsync(shelf, $"{Plugin}.caption"));

            InstallPlugins(TestPlugin);
            await Services.StartPluginFieldIndexAsync();
            Assert.Equal("{\"caption\":\"apple\"}", (await MetadataAsync(shelf, file)).Plugins[Plugin].FileDocument?.Json);
            Assert.Equal("/a.txt", Assert.Single((await FileService.SearchAsync(shelf, new FileSearch { Text = "apple" })).Files).Path);
        }

        [Fact]
        public async Task GetAsync_ContentsAndFileDocuments_AreBothThereWithTheirFields()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");
            await Services.RecordContentDocumentAsync(file.Sha256, "{\"width\":10,\"caption\":\"of the contents, where it is not declared\"}");
            await Services.SetFileDocumentAsync(file.Id, "{\"rating\":4,\"width\":99}");

            PluginFileMetadata kept = (await MetadataAsync(shelf, file)).Plugins[Plugin];

            Assert.Equal(("Test plugin", 1), (kept.Name, kept.FileVersion));
            Assert.Equal(10, kept.ContentsDocument?.ToElement().GetProperty("width").GetInt32());
            Assert.Equal([("rating", 4d), ("width", 10d)], kept.Fields.Select(field => (field.Name, field.Value.Number!.Value)));
        }

        [Fact]
        public async Task GetAsync_EncryptedFile_HasNoContentsDocumentEvenWhenAPlainFileHoldsTheBytes()
        {
            Shelf shelf = await CreateShelfAsync();
            byte[] bytes = Encoding.ASCII.GetBytes("age-encryption.org/v1\nstored as plain as well");
            ShelfFile plain = await StoreAsync(shelf, "/plain.txt", bytes, "text/plain");
            ShelfFile secret = await StoreAsync(shelf, "/secret.txt", bytes, "text/plain", encrypted: true);
            await Services.RecordContentDocumentAsync(plain.Sha256, "{\"width\":10}");
            await Services.SetFileDocumentAsync(secret.Id, "{\"rating\":4}");

            PluginFileMetadata kept = (await MetadataAsync(shelf, secret)).Plugins[Plugin];

            Assert.Null(kept.ContentsDocument);
            Assert.Equal(["rating"], kept.Fields.Select(field => field.Name));
            Assert.NotNull((await MetadataAsync(shelf, plain)).Plugins[Plugin].ContentsDocument);
        }

        [Fact]
        public async Task ListDeletionAsync_DeletedFile_CarriesTheFieldsOfItsContents()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");
            await Services.RecordContentDocumentAsync(file.Sha256, "{\"words\":\"apple\"}");
            TrashEntry deleted = await FileService.DeleteFileAsync(shelf, EntryReference.ForId(file.Id));

            DeletionTree tree = await TrashService.ListDeletionAsync(shelf, deleted.DeletionId, new TreeQuery());

            Assert.Equal("apple", Assert.Single(tree.Tree.Files).PluginFields![Plugin]["words"].Text);
        }

        private Task<FileMetadata> MetadataAsync(Shelf shelf, ShelfFile file)
        {
            return Services.GetRequiredService<FileMetadataService>().GetAsync(shelf, EntryReference.ForId(file.Id));
        }

        private async Task<IEnumerable<string>> FindAsync(Shelf shelf, string filter)
        {
            FileSearchResults found = await FileService.SearchAsync(shelf, new FileSearch { PluginFields = TestMetadataPlugins.Filters(filter) });
            return found.Files.Select(file => file.Path);
        }
    }
}
