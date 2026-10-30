using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.Storage
{
    public class FileServiceTests : ServiceTestBase
    {
        [Fact]
        public async Task ListDirectoryAsync_Root_ListsDirectoriesAndFilesByName()
        {
            Shelf shelf = await CreateShelfAsync();
            await StoreTextAsync(shelf, "/b.txt", "b");
            await StoreTextAsync(shelf, "/a.txt", "a");
            await StoreTextAsync(shelf, "/sub/c.txt", "c");

            DirectoryListing listing = await FileService.ListDirectoryAsync(shelf, EntryReference.ForPath("/"));

            Assert.Equal("/", listing.Directory.Path);
            Assert.Null(listing.Directory.ParentId);
            Assert.Equal(new[] { "/sub" }, listing.Directories.Select(directory => directory.Path));
            Assert.Equal(new[] { "/a.txt", "/b.txt" }, listing.Files.Select(file => file.Path));
        }

        [Fact]
        public async Task ListDirectoryAsync_ById_ListsThatDirectory()
        {
            Shelf shelf = await CreateShelfAsync();
            await StoreTextAsync(shelf, "/sub/c.txt", "c");
            DirectoryListing root = await FileService.ListDirectoryAsync(shelf, EntryReference.ForPath("/"));

            DirectoryListing listing = await FileService.ListDirectoryAsync(shelf, EntryReference.ForId(root.Directories[0].Id));

            Assert.Equal("/sub", listing.Directory.Path);
            Assert.Equal("/sub/c.txt", Assert.Single(listing.Files).Path);
        }

        [Fact]
        public async Task ListDirectoryAsync_MissingPath_ThrowsNotFound()
        {
            Shelf shelf = await CreateShelfAsync();

            await Assert.ThrowsAsync<NotFoundException>(() => FileService.ListDirectoryAsync(shelf, EntryReference.ForPath("/nope")));
        }

        [Fact]
        public async Task GetFileAsync_ByIdFromAnotherShelf_ThrowsNotFound()
        {
            Shelf mine = await CreateShelfAsync("mine");
            Shelf other = await CreateShelfAsync("other");
            ShelfFile file = await StoreTextAsync(other, "/secret.txt", "x");

            await Assert.ThrowsAsync<NotFoundException>(() => FileService.GetFileAsync(mine, EntryReference.ForId(file.Id)));
        }

        [Fact]
        public async Task GetContentAsync_ByPath_PointsAtTheObject()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "contents");

            FileContent content = await FileService.GetContentAsync(shelf, EntryReference.ForPath("/a.txt"));

            Assert.Equal(file.Id, content.File.Id);
            Assert.Equal(ObjectStore.GetPath(file.Sha256), content.ObjectPath);
        }
    }
}
