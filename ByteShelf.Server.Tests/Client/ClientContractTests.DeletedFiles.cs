using System.Text;
using ByteShelfClient;
using ByteShelfClient.Encryption;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.Client
{
    /// <summary>Reading what a deletion removed without restoring it, the same in both clients.</summary>
    public abstract partial class ClientContractTests
    {
        [Fact]
        public async Task ListDeletionAsync_AFileDeletion_SaysWhatItTookAndListsTheFileByThePathItHad()
        {
            await UploadTextAsync("/a/b.txt", "b");
            TrashEntry deleted = await Shelf.DeleteFileAsync("/a/b.txt");

            DeletionTree listed = await Shelf.ListDeletionAsync(deleted.DeletionId);

            Assert.Equal((TrashEntryKind.File, deleted.EntryId, "/a/b.txt"), (listed.Deletion.Kind, listed.Deletion.EntryId, listed.Deletion.Path));
            Assert.Equal(("/a", (string?)null), (listed.Tree.Directory.Path, listed.Tree.NextCursor));
            Assert.Equal(new[] { "/a/b.txt" }, PathsOf(listed.Tree));
        }

        [Fact]
        public async Task ListDeletionAsync_ADirectoryDeletion_ListsEverythingItTookAPageAtATime()
        {
            await UploadTextAsync("/keep.txt", "k");
            await UploadTextAsync("/photos/a.jpg", "a");
            await UploadTextAsync("/photos/2026/b.jpg", "b");
            await Shelf.CreateDirectoryAsync("/photos/empty");
            await UploadTextAsync("/photos/earlier.jpg", "e");
            await Shelf.DeleteFileAsync("/photos/earlier.jpg");
            TrashEntry deleted = await Shelf.DeleteDirectoryAsync("/photos");

            DirectoryTree first = (await Shelf.ListDeletionAsync(deleted.DeletionId, limit: 3)).Tree;
            DirectoryTree second = (await Shelf.ListDeletionAsync(deleted.DeletionId, after: first.NextCursor, limit: 3)).Tree;

            Assert.Equal("/", first.Directory.Path);
            Assert.Equal(new[] { "/photos", "/photos/2026", "/photos/2026/b.jpg" }, PathsOf(first));
            Assert.Equal(new[] { "/photos/a.jpg", "/photos/empty" }, PathsOf(second));
            Assert.Null(second.NextCursor);
        }

        [Fact]
        public async Task ListDeletionAsync_ADirectoryDeletedEarlierInsideOneDeletedLater_IsLeftOut()
        {
            await UploadTextAsync("/photos/a.jpg", "a");
            await UploadTextAsync("/photos/old/b.jpg", "b");
            await Shelf.DeleteDirectoryAsync("/photos/old");
            TrashEntry deleted = await Shelf.DeleteDirectoryAsync("/photos");

            DeletionTree listed = await Shelf.ListDeletionAsync(deleted.DeletionId);

            Assert.Equal(new[] { "/photos", "/photos/a.jpg" }, PathsOf(listed.Tree));
        }

        [Fact]
        public async Task ListDeletionAsync_AFileDeletedFromADirectoryDeletedSince_KeepsThePathItHad()
        {
            await UploadTextAsync("/a/b.txt", "b");
            TrashEntry deleted = await Shelf.DeleteFileAsync("/a/b.txt");
            await Shelf.DeleteDirectoryAsync("/a");

            DeletionTree listed = await Shelf.ListDeletionAsync(deleted.DeletionId);

            Assert.Equal("/a", listed.Tree.Directory.Path);
            Assert.Equal(new[] { "/a/b.txt" }, PathsOf(listed.Tree));
        }

        [Theory]
        [InlineData("/photos/2026", new[] { "/photos/2026", "/photos/2026/b.jpg" })]
        [InlineData("/photos/a.jpg", new[] { "/photos/a.jpg" })]
        public async Task ListDeletionAsync_UnderAPathItTook_ListsWhatItTookThereAndBelow(string under, string[] expected)
        {
            await UploadTextAsync("/photos/a.jpg", "a");
            await UploadTextAsync("/photos/2026/b.jpg", "b");
            await UploadTextAsync("/photos/2026-old.jpg", "c");
            TrashEntry deleted = await Shelf.DeleteDirectoryAsync("/photos");

            DeletionTree listed = await Shelf.ListDeletionAsync(deleted.DeletionId, under);

            Assert.Equal(expected, PathsOf(listed.Tree));
        }

        [Fact]
        public async Task ListDeletionAsync_UnderAPathItDidNotTake_IsNotFound()
        {
            await UploadTextAsync("/photos/a.jpg", "a");
            await UploadTextAsync("/kept.jpg", "k");
            TrashEntry deleted = await Shelf.DeleteDirectoryAsync("/photos");

            ByteShelfException exception = await RefusalOf(Shelf.ListDeletionAsync(deleted.DeletionId, "/kept.jpg"));

            Assert.Equal(ErrorCodes.NotFound, exception.Code);
        }

        [Fact]
        public async Task ListDeletionAsync_AFileFromAnEncryptedFolder_NamesTheFolderAndItsCheck()
        {
            await Shelf.CreateDirectoryAsync("/secret");
            await Shelf.EncryptDirectoryAsync("/secret", await CheckAsync());
            await UploadEncryptedAsync("/secret/a.txt", "a");
            TrashEntry deleted = await Shelf.DeleteFileAsync("/secret/a.txt");

            EncryptedFolder folder = (await Shelf.ListDeletionAsync(deleted.DeletionId)).EncryptedBy!;

            Assert.Equal("/secret", folder.Directory.Path);
            Assert.True(await FolderPassword.MatchesAsync(folder, FolderPasswordText));
        }

        [Fact]
        public async Task ListDeletionAsync_AnEncryptedFolderDeletedWhole_NamesItself()
        {
            await Shelf.CreateDirectoryAsync("/secret");
            await Shelf.EncryptDirectoryAsync("/secret", await CheckAsync());
            await UploadEncryptedAsync("/secret/a.txt", "a");
            TrashEntry deleted = await Shelf.DeleteDirectoryAsync("/secret");

            DeletionTree listed = await Shelf.ListDeletionAsync(deleted.DeletionId);

            Assert.Equal(deleted.EntryId, listed.EncryptedBy?.Directory.Id);
        }

        [Fact]
        public async Task ListDeletionAsync_UnderAnEncryptedFolderInsideAPlainOne_NamesTheEncryptedFolder()
        {
            await UploadTextAsync("/photos/a.jpg", "a");
            await Shelf.CreateDirectoryAsync("/photos/secret");
            await Shelf.EncryptDirectoryAsync("/photos/secret", await CheckAsync());
            await UploadEncryptedAsync("/photos/secret/b.txt", "b");
            TrashEntry deleted = await Shelf.DeleteDirectoryAsync("/photos");

            DeletionTree whole = await Shelf.ListDeletionAsync(deleted.DeletionId);
            DeletionTree inside = await Shelf.ListDeletionAsync(deleted.DeletionId, "/photos/secret/b.txt");

            Assert.Equal(((string?)null, "/photos/secret"), (whole.EncryptedBy?.Directory.Path, inside.EncryptedBy?.Directory.Path));
        }

        [Fact]
        public async Task OpenDeletedReadAsync_AFileTheDeletionTook_ReadsItsContents()
        {
            await UploadTextAsync("/photos/a.txt", "the contents");
            TrashEntry deleted = await Shelf.DeleteDirectoryAsync("/photos");
            ShelfFile file = Assert.Single((await Shelf.ListDeletionAsync(deleted.DeletionId)).Tree.Files);

            await using Stream content = await Shelf.OpenDeletedReadAsync(deleted.DeletionId, file.Id);

            Assert.Equal("the contents", await new StreamReader(content, Encoding.UTF8).ReadToEndAsync());
        }

        [Fact]
        public async Task ListDeletionAsync_NoSuchDeletion_IsNotFound()
        {
            ByteShelfException exception = await RefusalOf(Shelf.ListDeletionAsync(Guid.NewGuid()));

            Assert.Equal(ErrorCodes.NotFound, exception.Code);
        }

        [Fact]
        public async Task OpenDeletedReadAsync_AFileAnotherDeletionTook_IsNotFound()
        {
            await UploadTextAsync("/a.txt", "a");
            await UploadTextAsync("/b.txt", "b");
            TrashEntry first = await Shelf.DeleteFileAsync("/a.txt");
            TrashEntry second = await Shelf.DeleteFileAsync("/b.txt");

            ByteShelfException exception = await RefusalOf(Shelf.OpenDeletedReadAsync(first.DeletionId, second.EntryId));

            Assert.Equal(ErrorCodes.NotFound, exception.Code);
        }

        // The server's own 400 for such a limit is TreeQuery.Normalize's, shared with the live tree, which
        // has no HTTP test of it either: this test covers the rule both use.
        [Fact]
        public async Task ListDeletionAsync_ALimitOverTheMost_ThrowsBeforeSending()
        {
            await Assert.ThrowsAsync<ArgumentException>(() => Shelf.ListDeletionAsync(Guid.NewGuid(), limit: TreeQuery.MaxLimit + 1));
        }
    }
}
