using ByteShelf.Server.Access.Models;
using ByteShelf.Server.Access.Services;
using ByteShelf.Server.ChangeLog.Models;
using ByteShelf.Server.Shared.Auth;
using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.Access
{
    public class ShelfKeyServiceTests : ServiceTestBase
    {
        [Fact]
        public async Task CreateAsync_Key_AnswersASecretThatFindsIt()
        {
            Shelf shelf = await CreateShelfAsync();

            CreatedShelfKey created = await KeyService.CreateAsync(shelf, "  backup agent  ", ShelfKeyPermission.AddOnly);

            Assert.StartsWith(ShelfKeys.SecretPrefix, created.Secret, StringComparison.Ordinal);
            Assert.Equal(("backup agent", ShelfKeyPermission.AddOnly), (created.Key.Name, created.Key.Permission));
            Assert.Equal(created.Key.Id, (await KeyService.FindBySecretAsync(created.Secret))?.Id);
        }

        [Fact]
        public async Task CreateAsync_Key_StoresAndLogsOnlyTheHashOfTheSecret()
        {
            Shelf shelf = await CreateShelfAsync();
            long before = await ChangeLog.GetLatestSequenceAsync();

            CreatedShelfKey created = await KeyService.CreateAsync(shelf, "app", ShelfKeyPermission.Read);

            ChangeLogEntry entry = Assert.Single(await ChangeLog.GetAfterAsync(before, 100));
            Assert.Equal(ChangeEntityType.ShelfKey, entry.EntityType);
            Assert.Equal(Sha256Of(created.Secret), ChangePayload.Deserialize<ShelfKey>(entry.Payload!).SecretSha256);
            Assert.DoesNotContain(created.Secret[ShelfKeys.SecretPrefix.Length..], entry.Payload!, StringComparison.Ordinal);
        }

        [Fact]
        public async Task CreateAsync_UndefinedPermission_ThrowsArgumentException()
        {
            Shelf shelf = await CreateShelfAsync();

            await Assert.ThrowsAsync<ArgumentException>(() => KeyService.CreateAsync(shelf, "app", (ShelfKeyPermission)42));
        }

        [Fact]
        public async Task CreateAsync_ShelfDeletedMeanwhile_ThrowsNotFound()
        {
            Shelf shelf = await CreateShelfAsync();
            await ShelfService.DeleteAsync(shelf);

            await Assert.ThrowsAsync<NotFoundException>(() => KeyService.CreateAsync(shelf, "app", ShelfKeyPermission.Read));
        }

        [Fact]
        public async Task FindBySecretAsync_SecretOfNoKey_ReturnsNull()
        {
            Assert.Null(await KeyService.FindBySecretAsync(RandomSecret.Generate(ShelfKeys.SecretPrefix)));
        }

        [Fact]
        public async Task DeleteAsync_Key_MakesItsSecretFindNothing()
        {
            Shelf shelf = await CreateShelfAsync();
            CreatedShelfKey created = await KeyService.CreateAsync(shelf, "app", ShelfKeyPermission.Read);

            await KeyService.DeleteAsync(shelf, created.Key.Id);

            Assert.Null(await KeyService.FindBySecretAsync(created.Secret));
            Assert.Empty(await KeyService.GetForShelfAsync(shelf));
        }

        [Fact]
        public async Task DeleteAsync_KeyOfAnotherShelf_ThrowsNotFoundAndKeepsIt()
        {
            Shelf mine = await CreateShelfAsync("mine");
            Shelf other = await CreateShelfAsync("other");
            CreatedShelfKey created = await KeyService.CreateAsync(other, "app", ShelfKeyPermission.Read);

            await Assert.ThrowsAsync<NotFoundException>(() => KeyService.DeleteAsync(mine, created.Key.Id));

            Assert.NotNull(await KeyService.FindBySecretAsync(created.Secret));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("tab\there")]
        public async Task CreateAsync_NameNotAllowed_ThrowsArgumentException(string name)
        {
            Shelf shelf = await CreateShelfAsync();

            await Assert.ThrowsAsync<ArgumentException>(() => KeyService.CreateAsync(shelf, name, ShelfKeyPermission.Read));
        }

        [Fact]
        public async Task CreateAsync_NameTooLong_ThrowsArgumentException()
        {
            Shelf shelf = await CreateShelfAsync();

            await Assert.ThrowsAsync<ArgumentException>(() => KeyService.CreateAsync(shelf, new string('k', ShelfKeys.MaxNameLength + 1), ShelfKeyPermission.Read));
        }
    }
}
