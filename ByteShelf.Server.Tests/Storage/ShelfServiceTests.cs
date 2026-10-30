using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.Storage
{
    public class ShelfServiceTests : ServiceTestBase
    {
        [Fact]
        public async Task CreateAsync_NewName_CreatesShelfWithAnEmptyRoot()
        {
            Shelf shelf = await ShelfService.CreateAsync("backups", 1024);

            DirectoryListing root = await FileService.ListDirectoryAsync(shelf, EntryReference.ForPath("/"));

            Assert.Equal("backups", shelf.Name);
            Assert.Equal(1024, shelf.QuotaBytes);
            Assert.Equal(TestInstants.Now, shelf.CreatedAt);
            Assert.Empty(root.Directories);
            Assert.Empty(root.Files);
        }

        [Fact]
        public async Task CreateAsync_NameTaken_ThrowsConflict()
        {
            await ShelfService.CreateAsync("backups", null);

            await Assert.ThrowsAsync<ConflictException>(() => ShelfService.CreateAsync("backups", null));
        }

        [Theory]
        [InlineData("Backups")]
        [InlineData("-backups")]
        [InlineData("back ups")]
        [InlineData("")]
        public async Task CreateAsync_NameNotAllowed_ThrowsArgumentException(string name)
        {
            await Assert.ThrowsAsync<ArgumentException>(() => ShelfService.CreateAsync(name, null));
        }

        [Fact]
        public async Task GetByNameAsync_Missing_ThrowsNotFound()
        {
            await Assert.ThrowsAsync<NotFoundException>(() => ShelfService.GetByNameAsync("nope"));
        }
    }
}
