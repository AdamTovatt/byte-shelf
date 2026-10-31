using System.Text;
using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.Storage
{
    public class DirectoryServiceTests : ServiceTestBase
    {
        [Fact]
        public async Task ListAsync_Root_ListsDirectoriesAndFilesByName()
        {
            Shelf shelf = await CreateShelfAsync();
            await StoreTextAsync(shelf, "/b.txt", "b");
            await StoreTextAsync(shelf, "/a.txt", "a");
            await StoreTextAsync(shelf, "/sub/c.txt", "c");

            DirectoryListing listing = await DirectoryService.ListAsync(shelf, EntryReference.ForPath("/"));

            Assert.Equal("/", listing.Directory.Path);
            Assert.Null(listing.Directory.ParentId);
            Assert.Equal(new[] { "/sub" }, listing.Directories.Select(directory => directory.Path));
            Assert.Equal(new[] { "/a.txt", "/b.txt" }, listing.Files.Select(file => file.Path));
        }

        [Fact]
        public async Task ListAsync_ById_ListsThatDirectory()
        {
            Shelf shelf = await CreateShelfAsync();
            await StoreTextAsync(shelf, "/sub/c.txt", "c");
            DirectoryListing root = await DirectoryService.ListAsync(shelf, EntryReference.ForPath("/"));

            DirectoryListing listing = await DirectoryService.ListAsync(shelf, EntryReference.ForId(root.Directories[0].Id));

            Assert.Equal("/sub", listing.Directory.Path);
            Assert.Equal("/sub/c.txt", Assert.Single(listing.Files).Path);
        }

        [Fact]
        public async Task ListAsync_MissingPath_ThrowsNotFound()
        {
            Shelf shelf = await CreateShelfAsync();

            await Assert.ThrowsAsync<NotFoundException>(() => DirectoryService.ListAsync(shelf, EntryReference.ForPath("/nope")));
        }

        [Fact]
        public async Task CreateAsync_NestedPath_CreatesEachLevel()
        {
            Shelf shelf = await CreateShelfAsync();

            ShelfDirectory created = await DirectoryService.CreateAsync(shelf, "/a/b/c");

            Assert.Equal("/a/b/c", created.Path);
            Assert.Equal("c", created.Name);
            DirectoryListing middle = await DirectoryService.ListAsync(shelf, EntryReference.ForPath("/a/b"));
            Assert.Equal(created.Id, Assert.Single(middle.Directories).Id);
        }

        [Fact]
        public async Task CreateAsync_DirectoryExists_AnswersItUnchanged()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfDirectory first = await DirectoryService.CreateAsync(shelf, "/a");

            ShelfDirectory second = await DirectoryService.CreateAsync(shelf, "/a");

            Assert.Equal(first.Id, second.Id);
        }

        [Fact]
        public async Task CreateAsync_FileInTheWay_ThrowsConflict()
        {
            Shelf shelf = await CreateShelfAsync();
            await StoreTextAsync(shelf, "/a", "file");

            await Assert.ThrowsAsync<ConflictException>(() => DirectoryService.CreateAsync(shelf, "/a/b"));
        }

        [Fact]
        public async Task DeleteAsync_Directory_TrashesEverythingUnderItAsOneDeletion()
        {
            Shelf shelf = await CreateShelfAsync();
            const string nested = "aa";
            const string direct = "bbb";
            await StoreTextAsync(shelf, "/photos/2026/a.jpg", nested);
            await StoreTextAsync(shelf, "/photos/b.jpg", direct);

            TrashEntry deletion = await DirectoryService.DeleteAsync(shelf, EntryReference.ForPath("/photos"));

            Assert.Equal(TrashEntryKind.Directory, deletion.Kind);
            Assert.Equal("/photos", deletion.Path);
            Assert.Equal(2, deletion.FileCount);
            Assert.Equal(nested.Length + direct.Length, deletion.SizeBytes);
            Assert.Single(await TrashService.GetTrashAsync(shelf));
            Assert.Empty((await DirectoryService.ListAsync(shelf, EntryReference.ForPath("/"))).Directories);
        }

        [Fact]
        public async Task DeleteAsync_ThenRestore_BringsBackTheWholeSubtree()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/photos/2026/a.jpg", "a");
            TrashEntry deletion = await DirectoryService.DeleteAsync(shelf, EntryReference.ForPath("/photos"));

            await TrashService.RestoreAsync(shelf, deletion.DeletionId);

            Assert.Equal(file.Id, (await FileService.GetFileAsync(shelf, EntryReference.ForPath("/photos/2026/a.jpg"))).Id);
        }

        [Fact]
        public async Task DeleteAsync_FileDeletedBefore_StaysItsOwnDeletion()
        {
            Shelf shelf = await CreateShelfAsync();
            await StoreTextAsync(shelf, "/photos/kept.jpg", "k");
            await StoreTextAsync(shelf, "/photos/gone.jpg", "g");
            TrashEntry fileDeletion = await FileService.DeleteFileAsync(shelf, EntryReference.ForPath("/photos/gone.jpg"));
            TrashEntry directoryDeletion = await DirectoryService.DeleteAsync(shelf, EntryReference.ForPath("/photos"));

            await TrashService.RestoreAsync(shelf, directoryDeletion.DeletionId);

            DirectoryListing photos = await DirectoryService.ListAsync(shelf, EntryReference.ForPath("/photos"));
            Assert.Equal("kept.jpg", Assert.Single(photos.Files).Name);
            Assert.Equal(fileDeletion.DeletionId, Assert.Single(await TrashService.GetTrashAsync(shelf)).DeletionId);
        }

        [Fact]
        public async Task DeleteAsync_SubdirectoryDeletedBefore_StaysItsOwnDeletion()
        {
            Shelf shelf = await CreateShelfAsync();
            await StoreTextAsync(shelf, "/photos/2025/old.jpg", "o");
            await StoreTextAsync(shelf, "/photos/new.jpg", "n");
            TrashEntry subdirectoryDeletion = await DirectoryService.DeleteAsync(shelf, EntryReference.ForPath("/photos/2025"));
            TrashEntry directoryDeletion = await DirectoryService.DeleteAsync(shelf, EntryReference.ForPath("/photos"));

            await TrashService.RestoreAsync(shelf, directoryDeletion.DeletionId);

            DirectoryListing photos = await DirectoryService.ListAsync(shelf, EntryReference.ForPath("/photos"));
            Assert.Empty(photos.Directories);
            Assert.Equal("new.jpg", Assert.Single(photos.Files).Name);
            TrashEntry left = Assert.Single(await TrashService.GetTrashAsync(shelf));
            Assert.Equal((subdirectoryDeletion.DeletionId, "/photos/2025", 1), (left.DeletionId, left.Path, left.FileCount));
        }

        [Fact]
        public async Task DeleteAsync_Root_ThrowsArgumentException()
        {
            Shelf shelf = await CreateShelfAsync();

            await Assert.ThrowsAsync<ArgumentException>(() => DirectoryService.DeleteAsync(shelf, EntryReference.ForPath("/")));
        }

        [Fact]
        public async Task MoveAsync_Directory_CarriesItsContentsWithoutTouchingObjects()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/drafts/2026/report.txt", "report");

            ShelfDirectory moved = await DirectoryService.MoveAsync(shelf, EntryReference.ForPath("/drafts"), "/archive/drafts-old");

            Assert.Equal("/archive/drafts-old", moved.Path);
            ShelfFile after = await FileService.GetFileAsync(shelf, EntryReference.ForPath("/archive/drafts-old/2026/report.txt"));
            Assert.Equal(file.Id, after.Id);
            Assert.Equal("report", await ReadObjectTextAsync(after.Sha256));
            await Assert.ThrowsAsync<NotFoundException>(() => DirectoryService.ListAsync(shelf, EntryReference.ForPath("/drafts")));
        }

        [Fact]
        public async Task MoveAsync_IntoItself_ThrowsArgumentExceptionAndCreatesNothing()
        {
            Shelf shelf = await CreateShelfAsync();
            await DirectoryService.CreateAsync(shelf, "/a");

            await Assert.ThrowsAsync<ArgumentException>(() => DirectoryService.MoveAsync(shelf, EntryReference.ForPath("/a"), "/a/b/a"));

            Assert.Empty((await DirectoryService.ListAsync(shelf, EntryReference.ForPath("/a"))).Directories);
        }

        [Fact]
        public async Task MoveAsync_ToTakenName_ThrowsConflict()
        {
            Shelf shelf = await CreateShelfAsync();
            await DirectoryService.CreateAsync(shelf, "/a");
            await StoreTextAsync(shelf, "/b", "file");

            await Assert.ThrowsAsync<ConflictException>(() => DirectoryService.MoveAsync(shelf, EntryReference.ForPath("/a"), "/b"));
        }

        [Fact]
        public async Task MoveAsync_SiblingWithSharedPrefix_IsAllowed()
        {
            Shelf shelf = await CreateShelfAsync();
            await DirectoryService.CreateAsync(shelf, "/a");

            ShelfDirectory moved = await DirectoryService.MoveAsync(shelf, EntryReference.ForPath("/a"), "/ab");

            Assert.Equal("/ab", moved.Path);
        }

        [Fact]
        public async Task MoveAsync_ToItsOwnPath_ChangesNothing()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfDirectory directory = await DirectoryService.CreateAsync(shelf, "/a");
            long before = await ChangeLog.GetLatestSequenceAsync();

            ShelfDirectory same = await DirectoryService.MoveAsync(shelf, EntryReference.ForId(directory.Id), "a/");

            Assert.Equal("/a", same.Path);
            Assert.Empty(await ChangeLog.GetAfterAsync(before, 100));
        }

        [Fact]
        public async Task MoveAsync_ToTheRoot_ThrowsArgumentException()
        {
            Shelf shelf = await CreateShelfAsync();
            await DirectoryService.CreateAsync(shelf, "/a");

            await Assert.ThrowsAsync<ArgumentException>(() => DirectoryService.MoveAsync(shelf, EntryReference.ForPath("/a"), "/"));
        }

        [Fact]
        public async Task MoveAsync_FileWhereADestinationDirectoryWouldGo_ThrowsConflict()
        {
            Shelf shelf = await CreateShelfAsync();
            await DirectoryService.CreateAsync(shelf, "/a");
            await StoreTextAsync(shelf, "/archive", "a file");

            await Assert.ThrowsAsync<ConflictException>(() => DirectoryService.MoveAsync(shelf, EntryReference.ForPath("/a"), "/archive/a"));
        }

        [Fact]
        public async Task MoveAsync_PathInTheTrashWouldGrowTooLong_ThrowsArgumentException()
        {
            Shelf shelf = await CreateShelfAsync();
            string deepPath = NestedPath("/d", 4000, 'a');
            await StoreTextAsync(shelf, deepPath, "x");
            await FileService.DeleteFileAsync(shelf, EntryReference.ForPath(deepPath));

            // The file's path would become 101 + 3998 = 4099 characters.
            await Assert.ThrowsAsync<ArgumentException>(() => DirectoryService.MoveAsync(shelf, EntryReference.ForPath("/d"), "/" + new string('m', 100)));
        }

        [Fact]
        public async Task MoveAsync_LongPathOfWideCharactersThatStillFits_IsAllowed()
        {
            Shelf shelf = await CreateShelfAsync();
            // Two UTF-8 bytes each, so the path is longer in bytes than the room left, though not in characters.
            string deepPath = NestedPath("/d", 4000, '\x00e4');
            await StoreTextAsync(shelf, deepPath, "x");

            ShelfDirectory moved = await DirectoryService.MoveAsync(shelf, EntryReference.ForPath("/d"), "/" + new string('m', 90));

            string movedPath = moved.Path + deepPath["/d".Length..];
            Assert.Equal(4089, movedPath.Length);
            await FileService.GetFileAsync(shelf, EntryReference.ForPath(movedPath));
        }

        [Fact]
        public async Task MoveAsync_Root_ThrowsArgumentException()
        {
            Shelf shelf = await CreateShelfAsync();

            await Assert.ThrowsAsync<ArgumentException>(() => DirectoryService.MoveAsync(shelf, EntryReference.ForPath("/"), "/elsewhere"));
        }

        /// <summary>
        /// <paramref name="prefix"/> followed by directories of at most 200 <paramref name="filler"/>
        /// characters, <paramref name="length"/> characters in all.
        /// </summary>
        private static string NestedPath(string prefix, int length, char filler)
        {
            StringBuilder path = new StringBuilder(prefix);
            while (path.Length < length)
            {
                int nameLength = Math.Min(200, length - path.Length - 1);
                path.Append(ShelfPath.Separator).Append(filler, nameLength);
            }

            Assert.Equal(length, path.Length);
            return path.ToString();
        }
    }
}
