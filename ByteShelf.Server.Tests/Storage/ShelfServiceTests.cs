using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelf.Server.Users.Models;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.Storage
{
    public class ShelfServiceTests : ServiceTestBase
    {
        [Fact]
        public async Task CreateAsync_NewName_CreatesShelfWithAnEmptyRoot()
        {
            Shelf shelf = await ShelfService.CreateAsync("backups", 1024);

            DirectoryListing root = await DirectoryService.ListAsync(shelf, EntryReference.ForPath("/"));

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
        public async Task UpdateAsync_NewNameAndQuota_ReplacesBoth()
        {
            Shelf shelf = await ShelfService.CreateAsync("backups", 1024);
            Clock.Advance(TimeSpan.FromMinutes(1));

            Shelf updated = await ShelfService.UpdateAsync(shelf, "archive", null);

            Assert.Equal(shelf.Id, updated.Id);
            Assert.Null((await ShelfService.GetByNameAsync("archive")).QuotaBytes);
            Assert.Equal(TestInstants.Now.AddMinutes(1), updated.UpdatedAt);
            await Assert.ThrowsAsync<NotFoundException>(() => ShelfService.GetByNameAsync("backups"));
        }

        [Fact]
        public async Task UpdateAsync_SameNameNewQuota_KeepsTheNameAndSetsTheQuota()
        {
            Shelf shelf = await ShelfService.CreateAsync("backups", 1024);

            await ShelfService.UpdateAsync(shelf, "backups", 2048);

            Shelf stored = await ShelfService.GetByNameAsync("backups");
            Assert.Equal((shelf.Id, 2048L), (stored.Id, stored.QuotaBytes));
        }

        [Fact]
        public async Task UpdateAsync_NameTaken_ThrowsConflict()
        {
            Shelf shelf = await ShelfService.CreateAsync("backups", null);
            await ShelfService.CreateAsync("archive", null);

            await Assert.ThrowsAsync<ConflictException>(() => ShelfService.UpdateAsync(shelf, "archive", null));
        }

        [Fact]
        public async Task DeleteAsync_EmptyShelf_RemovesItAndFreesTheName()
        {
            Shelf shelf = await ShelfService.CreateAsync("backups", null);

            await ShelfService.DeleteAsync(shelf);

            await Assert.ThrowsAsync<NotFoundException>(() => ShelfService.GetByNameAsync("backups"));
            Shelf again = await ShelfService.CreateAsync("backups", null);
            Assert.NotEqual(shelf.Id, again.Id);
        }

        [Fact]
        public async Task DeleteAsync_AlreadyDeleted_ThrowsNotFound()
        {
            Shelf shelf = await ShelfService.CreateAsync("backups", null);
            await ShelfService.DeleteAsync(shelf);

            await Assert.ThrowsAsync<NotFoundException>(() => ShelfService.DeleteAsync(shelf));
        }

        [Fact]
        public async Task DeleteAsync_AfterEmptyingTheTrash_RemovesIt()
        {
            Shelf shelf = await ShelfService.CreateAsync("backups", null);
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");
            await FileService.DeleteFileAsync(shelf, EntryReference.ForPath("/a.txt"));
            await TrashService.EmptyAsync(shelf, CancellationToken.None);

            await ShelfService.DeleteAsync(shelf);

            await Assert.ThrowsAsync<NotFoundException>(() => ShelfService.GetByNameAsync("backups"));
            Assert.False(ObjectStore.Exists(file.Sha256));
        }

        [Fact]
        public async Task DeleteAsync_UploadInProgress_ThrowsConflict()
        {
            Shelf shelf = await ShelfService.CreateAsync("backups", null);
            await UploadService.StartAsync(shelf, new StartUploadRequest { Path = "/a.bin", SizeBytes = 10 }, Uploader);

            await Assert.ThrowsAsync<ConflictException>(() => ShelfService.DeleteAsync(shelf));
        }

        [Fact]
        public async Task DeleteAsync_OnlyTrashLeft_ThrowsConflict()
        {
            Shelf shelf = await ShelfService.CreateAsync("backups", null);
            await StoreTextAsync(shelf, "/a.txt", "a");
            await FileService.DeleteFileAsync(shelf, EntryReference.ForPath("/a.txt"));

            await Assert.ThrowsAsync<ConflictException>(() => ShelfService.DeleteAsync(shelf));
        }

        [Fact]
        public async Task DeleteAsync_EmptyDirectoryLeft_ThrowsConflict()
        {
            Shelf shelf = await ShelfService.CreateAsync("backups", null);
            await DirectoryService.CreateAsync(shelf, "/empty");

            await Assert.ThrowsAsync<ConflictException>(() => ShelfService.DeleteAsync(shelf));
        }

        [Fact]
        public async Task DeleteAsync_ShelfWithGrantsAndKeys_RemovesThem()
        {
            Shelf shelf = await ShelfService.CreateAsync("backups", null);
            User user = await CreateUserAsync("user@example.com");
            await GrantService.SetAsync(shelf, user.Id, ShelfAccessPermission.Read);
            CreatedShelfKey key = await KeyService.CreateAsync(shelf, "app", ShelfKeyPermission.Read);

            await ShelfService.DeleteAsync(shelf);

            Assert.Null(await KeyService.FindBySecretAsync(key.Secret));
            Assert.Empty(await GrantService.GetForShelfAsync(shelf));
        }

        [Fact]
        public async Task GetByNameAsync_Missing_ThrowsNotFound()
        {
            await Assert.ThrowsAsync<NotFoundException>(() => ShelfService.GetByNameAsync("nope"));
        }
    }
}
