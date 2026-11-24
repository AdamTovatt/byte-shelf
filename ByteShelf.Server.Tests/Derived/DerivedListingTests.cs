using System.Text.Json;
using ByteShelf.Server.Derived.Services;
using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;
using ByteShelfCommon.Serialization;

namespace ByteShelf.Server.Tests.Derived
{
    /// <summary>Which derived files listings, and files' own records, say there are.</summary>
    public sealed class DerivedListingTests : DerivedTestBase
    {
        [Fact]
        public async Task Listings_NameTheKindsThereAreWithTheirVersionAndMakeNothing()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile made = await StoreTextAsync(shelf, "/made.txt", "made");
            await StoreTextAsync(shelf, "/not.txt", "not made");
            ShelfFile failed = await StoreTextAsync(shelf, "/failed.txt", "throw");
            await GetAsync(shelf, made);
            await Assert.ThrowsAsync<NotFoundException>(() => GetAsync(shelf, failed));
            int runs = Plugins.Runs.Count;

            string[] listed =
            [
                Said((await DirectoryService.ListAsync(shelf, EntryReference.ForPath("/"))).Files),
                Said((await DirectoryService.ListTreeAsync(shelf, new TreeQuery())).Files),
                Said((await FileService.SearchAsync(shelf, new FileSearch { Text = "txt" })).Files),
            ];

            Assert.All(listed, said => Assert.Equal("/failed.txt: | /made.txt: Thumbnail test-maker-1, Preview test-maker-1 | /not.txt:", said));
            Assert.Equal(runs, Plugins.Runs.Count);
        }

        [Fact]
        public async Task AFilesOwnRecord_NamesThemAsAListingDoes()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile made = await StoreTextAsync(shelf, "/made.txt", "made");
            ShelfFile not = await StoreTextAsync(shelf, "/not.txt", "not made");
            await GetAsync(shelf, made);

            Assert.Equal("/made.txt: Thumbnail test-maker-1, Preview test-maker-1", Said([await FileService.GetFileAsync(shelf, EntryReference.ForId(made.Id))]));
            Assert.Equal("/not.txt:", Said([await FileService.GetFileAsync(shelf, EntryReference.ForId(not.Id))]));
        }

        [Fact]
        public async Task Listings_OfADeletion_NameThemToo()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");
            await GetAsync(shelf, file);
            TrashEntry deleted = await FileService.DeleteFileAsync(shelf, EntryReference.ForId(file.Id));

            DeletionTree tree = await TrashService.ListDeletionAsync(shelf, deleted.DeletionId, new TreeQuery());

            Assert.Equal("/a.txt: Thumbnail test-maker-1, Preview test-maker-1", Said(tree.Tree.Files));
        }

        [Fact]
        public async Task Listing_OnceItIsMadeAgainOrRemoved_SaysSoThoughItSaidOtherwiseBefore()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");
            Assert.Equal("/a.txt:", await ListAsync(shelf));
            await GetAsync(shelf, file);
            Assert.Equal("/a.txt: Thumbnail test-maker-1, Preview test-maker-1", await ListAsync(shelf));

            Plugins.All = [Plugins.Plugin(version: 2, kinds: [DerivedKind.Thumbnail])];
            Assert.Equal("/a.txt:", await ListAsync(shelf));
            await GetAsync(shelf, file);
            Assert.Equal("/a.txt: Thumbnail test-maker-2", await ListAsync(shelf));

            Plugins.All = [];
            Assert.Equal("/a.txt:", await ListAsync(shelf));
        }

        [Fact]
        public async Task Listing_AsItIsBeingKept_SaysSoOnceItIs()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");
            using (await Locks.LockAsync(file.Sha256))
            {
                // Listed by somebody else as whoever holds the lock has yet to write it.
                Assert.Equal("/a.txt:", await ListAsync(shelf));
                string source = Path.Combine(TestRoot.Root.StagingPath, "made");
                await File.WriteAllTextAsync(source, "made");
                Directory.CreateDirectory(Path.GetDirectoryName(Store.GetPath(DerivedKind.Thumbnail, file.Sha256))!);
                File.Move(source, Store.GetPath(DerivedKind.Thumbnail, file.Sha256));
                await File.WriteAllTextAsync(
                    Store.GetPath(DerivedKind.Thumbnail, file.Sha256) + ".json", """{"plugin":"test-maker","version":1,"outcome":{"was":"made","mediaType":"image/webp"}}""");
            }

            Assert.Equal("/a.txt: Thumbnail test-maker-1", await ListAsync(shelf));
        }

        [Fact]
        public async Task Listing_OfAnEncryptedFile_NamesNoneEvenWhenAPlainOneHasTheSameBytes()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile plain = await StoreAsync(shelf, "/plain.txt", AgeBytes, "text/plain");
            await StoreAsync(shelf, "/secret.txt", AgeBytes, "text/plain", encrypted: true);
            await StoreAsync(shelf, "/z.bin", AgeBytes, "application/octet-stream");
            await GetAsync(shelf, plain);

            Assert.Equal("/plain.txt: Thumbnail test-maker-1, Preview test-maker-1 | /secret.txt: | /z.bin:", await ListAsync(shelf));

            await EncryptInPlaceAsync(shelf, plain);
            Assert.Equal("/plain.txt: | /secret.txt: | /z.bin:", await ListAsync(shelf));
        }

        [Fact]
        public void ShelfFile_AsJson_NamesKindsAsPathsDo()
        {
            ShelfFile file = TestFiles.Record("/a", "a/b", new Dictionary<DerivedKind, string> { [DerivedKind.Thumbnail] = "images-1" });

            string json = JsonSerializer.Serialize(file, ByteShelfJsonContext.Default.ShelfFile);

            Assert.Contains("\"derived\":{\"thumbnail\":\"images-1\"}", json, StringComparison.Ordinal);
            Assert.Equal("images-1", JsonSerializer.Deserialize(json, ByteShelfJsonContext.Default.ShelfFile)!.Derived![DerivedKind.Thumbnail]);
        }

        private async Task<string> ListAsync(Shelf shelf)
        {
            return Said((await DirectoryService.ListAsync(shelf, EntryReference.ForPath("/"))).Files);
        }

        private static string Said(IEnumerable<ShelfFile> files)
        {
            return string.Join(" | ", files.OrderBy(file => file.Path, StringComparer.Ordinal).Select(file =>
                $"{file.Path}: {string.Join(", ", (file.Derived ?? new Dictionary<DerivedKind, string>()).Select(kind => $"{kind.Key} {kind.Value}"))}".TrimEnd()));
        }
    }
}
