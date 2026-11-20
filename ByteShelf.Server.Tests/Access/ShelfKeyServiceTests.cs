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
            Assert.Equal("backup agent", created.Key.Name);
            ShelfKeyGrantInfo grant = Assert.Single(created.Key.Grants);
            Assert.Equal((shelf.Id, shelf.Name, ShelfKeyPermission.AddOnly), (grant.ShelfId, grant.ShelfName, grant.Permission));
            Assert.Equal(created.Key.Id, (await KeyService.FindBySecretAsync(created.Secret))?.Id);
        }

        [Fact]
        public async Task CreateAsync_Key_LogsTheKeyWithOnlyTheHashOfTheSecretThenItsGrant()
        {
            Shelf shelf = await CreateShelfAsync();
            long before = await ChangeLog.GetLatestSequenceAsync();

            CreatedShelfKey created = await KeyService.CreateAsync(shelf, "app", ShelfKeyPermission.Read);

            IReadOnlyList<ChangeLogEntry> entries = await ChangeLog.GetAfterAsync(before, 100);
            Assert.Equal(new[] { ChangeEntityType.ShelfKey, ChangeEntityType.ShelfKeyGrant }, entries.Select(entry => entry.EntityType));
            Assert.Equal(Sha256Of(created.Secret), ChangePayload.Deserialize<ShelfKey>(entries[0].Payload!).SecretSha256);
            Assert.DoesNotContain(created.Secret[ShelfKeys.SecretPrefix.Length..], entries[0].Payload!, StringComparison.Ordinal);
            ShelfKeyGrant grant = ChangePayload.Deserialize<ShelfKeyGrant>(entries[1].Payload!);
            Assert.Equal((created.Key.Id, shelf.Id, ShelfKeyPermission.Read), (grant.KeyId, grant.ShelfId, grant.Permission));
        }

        [Fact]
        public async Task SetGrantAsync_AnotherShelf_GivesTheKeyItAndListsItOnBoth()
        {
            Shelf docs = await CreateShelfAsync("docs");
            Shelf photos = await CreateShelfAsync("photos");
            CreatedShelfKey created = await KeyService.CreateAsync(docs, "app", ShelfKeyPermission.Read);

            // Grants list oldest first and then by their random id: a later grant keeps docs before photos.
            Clock.Advance(TimeSpan.FromSeconds(1));

            ShelfKeyGrantInfo given = await KeyService.SetGrantAsync(photos, created.Key.Id, ShelfKeyPermission.AddOnly);

            Assert.Equal((created.Key.Id, "photos", ShelfKeyPermission.AddOnly), (given.KeyId, given.ShelfName, given.Permission));
            foreach (Shelf shelf in new[] { docs, photos })
            {
                ShelfKeyInfo listed = Assert.Single(await KeyService.GetForShelfAsync(shelf));
                Assert.Equal(
                    new[] { ("docs", ShelfKeyPermission.Read), ("photos", ShelfKeyPermission.AddOnly) },
                    listed.Grants.Select(grant => (grant.ShelfName, grant.Permission)));
            }
        }

        [Fact]
        public async Task SetGrantAsync_ShelfItHas_ChangesThePermissionAndLogsAnUpdate()
        {
            Shelf shelf = await CreateShelfAsync();
            CreatedShelfKey created = await KeyService.CreateAsync(shelf, "app", ShelfKeyPermission.Read);
            long before = await ChangeLog.GetLatestSequenceAsync();

            await KeyService.SetGrantAsync(shelf, created.Key.Id, ShelfKeyPermission.ReadWrite);

            ChangeLogEntry entry = Assert.Single(await ChangeLog.GetAfterAsync(before, 100));
            Assert.Equal((ChangeEntityType.ShelfKeyGrant, ChangeAction.Updated), (entry.EntityType, entry.Action));
            Assert.Equal(ShelfKeyPermission.ReadWrite, Assert.Single(await KeyService.GetGrantsAsync(created.Key.Id)).Permission);
        }

        [Fact]
        public async Task SetGrantAsync_SamePermission_ChangesAndLogsNothing()
        {
            Shelf shelf = await CreateShelfAsync();
            CreatedShelfKey created = await KeyService.CreateAsync(shelf, "app", ShelfKeyPermission.Read);
            long before = await ChangeLog.GetLatestSequenceAsync();

            await KeyService.SetGrantAsync(shelf, created.Key.Id, ShelfKeyPermission.Read);

            Assert.Empty(await ChangeLog.GetAfterAsync(before, 100));
        }

        [Fact]
        public async Task SetGrantAsync_UnknownKey_ThrowsNotFound()
        {
            Shelf shelf = await CreateShelfAsync();

            await Assert.ThrowsAsync<NotFoundException>(() => KeyService.SetGrantAsync(shelf, Guid.NewGuid(), ShelfKeyPermission.Read));
        }

        [Fact]
        public async Task SetGrantAsync_ShelfDeletedMeanwhile_ThrowsNotFound()
        {
            Shelf docs = await CreateShelfAsync("docs");
            Shelf photos = await CreateShelfAsync("photos");
            CreatedShelfKey created = await KeyService.CreateAsync(docs, "app", ShelfKeyPermission.Read);
            await ShelfService.DeleteAsync(photos);

            await Assert.ThrowsAsync<NotFoundException>(() => KeyService.SetGrantAsync(photos, created.Key.Id, ShelfKeyPermission.Read));
        }

        [Fact]
        public async Task GetGrantsAsync_UnknownKey_ThrowsNotFound()
        {
            await Assert.ThrowsAsync<NotFoundException>(() => KeyService.GetGrantsAsync(Guid.NewGuid()));
        }

        [Fact]
        public async Task SetGrantAsync_UndefinedPermission_ThrowsArgumentException()
        {
            Shelf shelf = await CreateShelfAsync();
            CreatedShelfKey created = await KeyService.CreateAsync(shelf, "app", ShelfKeyPermission.Read);

            await Assert.ThrowsAsync<ArgumentException>(() => KeyService.SetGrantAsync(shelf, created.Key.Id, (ShelfKeyPermission)42));
        }

        [Fact]
        public async Task RemoveGrantAsync_NotItsLast_KeepsTheKeyOnItsOtherShelf()
        {
            Shelf docs = await CreateShelfAsync("docs");
            Shelf photos = await CreateShelfAsync("photos");
            CreatedShelfKey created = await KeyService.CreateAsync(docs, "app", ShelfKeyPermission.Read);
            await KeyService.SetGrantAsync(photos, created.Key.Id, ShelfKeyPermission.Read);

            await KeyService.RemoveGrantAsync(docs, created.Key.Id);

            Assert.Equal(created.Key.Id, (await KeyService.FindBySecretAsync(created.Secret))?.Id);
            Assert.Empty(await KeyService.GetForShelfAsync(docs));
            Assert.Equal("photos", Assert.Single(await KeyService.GetGrantsAsync(created.Key.Id)).ShelfName);
        }

        [Fact]
        public async Task RemoveGrantAsync_ItsLast_DeletesTheKeyAndLogsBoth()
        {
            Shelf shelf = await CreateShelfAsync();
            CreatedShelfKey created = await KeyService.CreateAsync(shelf, "app", ShelfKeyPermission.Read);
            long before = await ChangeLog.GetLatestSequenceAsync();

            await KeyService.RemoveGrantAsync(shelf, created.Key.Id);

            Assert.Null(await KeyService.FindBySecretAsync(created.Secret));
            Assert.Equal(
                new[] { (ChangeEntityType.ShelfKeyGrant, ChangeAction.Purged), (ChangeEntityType.ShelfKey, ChangeAction.Purged) },
                (await ChangeLog.GetAfterAsync(before, 100)).Select(entry => (entry.EntityType, entry.Action)));
        }

        [Fact]
        public async Task DeleteAsync_KeyOnTwoShelves_DeletesItFromBoth()
        {
            Shelf docs = await CreateShelfAsync("docs");
            Shelf photos = await CreateShelfAsync("photos");
            CreatedShelfKey created = await KeyService.CreateAsync(docs, "app", ShelfKeyPermission.Read);
            await KeyService.SetGrantAsync(photos, created.Key.Id, ShelfKeyPermission.Read);

            await KeyService.DeleteAsync(created.Key.Id);

            Assert.Null(await KeyService.FindBySecretAsync(created.Secret));
            Assert.Empty(await KeyService.GetForShelfAsync(docs));
            Assert.Empty(await KeyService.GetForShelfAsync(photos));
        }

        [Fact]
        public async Task DeleteAsync_UnknownKey_ThrowsNotFound()
        {
            await Assert.ThrowsAsync<NotFoundException>(() => KeyService.DeleteAsync(Guid.NewGuid()));
        }

        [Fact]
        public async Task ShelfDeleted_DeletesTheKeysItWasTheLastShelfOfAndKeepsTheOthers()
        {
            Shelf docs = await CreateShelfAsync("docs");
            Shelf photos = await CreateShelfAsync("photos");
            CreatedShelfKey only = await KeyService.CreateAsync(docs, "only docs", ShelfKeyPermission.Read);
            CreatedShelfKey both = await KeyService.CreateAsync(docs, "both", ShelfKeyPermission.Read);
            await KeyService.SetGrantAsync(photos, both.Key.Id, ShelfKeyPermission.Read);

            await ShelfService.DeleteAsync(docs);

            Assert.Null(await KeyService.FindBySecretAsync(only.Secret));
            Assert.Equal(both.Key.Id, (await KeyService.FindBySecretAsync(both.Secret))?.Id);
            Assert.Equal("photos", Assert.Single(await KeyService.GetGrantsAsync(both.Key.Id)).ShelfName);
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
        public async Task RemoveGrantAsync_KeyOfAnotherShelf_ThrowsNotFoundAndKeepsIt()
        {
            Shelf mine = await CreateShelfAsync("mine");
            Shelf other = await CreateShelfAsync("other");
            CreatedShelfKey created = await KeyService.CreateAsync(other, "app", ShelfKeyPermission.Read);

            await Assert.ThrowsAsync<NotFoundException>(() => KeyService.RemoveGrantAsync(mine, created.Key.Id));

            Assert.Equal(created.Key.Id, (await KeyService.FindBySecretAsync(created.Secret))?.Id);
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
