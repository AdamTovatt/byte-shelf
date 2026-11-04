using ByteShelf.Server.ChangeLog.Models;
using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelf.Server.Users.Models;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.Access
{
    public class ShelfGrantServiceTests : ServiceTestBase
    {
        [Fact]
        public async Task SetAsync_NewGrant_IsListedWithTheUser()
        {
            Shelf shelf = await CreateShelfAsync();
            User user = await CreateUserAsync("user@example.com");

            await GrantService.SetAsync(shelf, user.Id, ShelfAccessPermission.Read);

            ShelfAccessGrant grant = Assert.Single(await GrantService.GetForShelfAsync(shelf));
            Assert.Equal((user.Id, "user@example.com", ShelfAccessPermission.Read), (grant.UserId, grant.Email, grant.Permission));
        }

        [Fact]
        public async Task SetAsync_ChangedPermission_ReplacesItAndLogsTheUpdate()
        {
            Shelf shelf = await CreateShelfAsync();
            User user = await CreateUserAsync("user@example.com");
            ShelfAccessGrant first = await GrantService.SetAsync(shelf, user.Id, ShelfAccessPermission.Read);
            Clock.Advance(TimeSpan.FromMinutes(1));
            long before = await ChangeLog.GetLatestSequenceAsync();

            ShelfAccessGrant second = await GrantService.SetAsync(shelf, user.Id, ShelfAccessPermission.ReadWrite);

            Assert.Equal((first.CreatedAt, ShelfAccessPermission.ReadWrite), (second.CreatedAt, second.Permission));
            ChangeLogEntry entry = Assert.Single(await ChangeLog.GetAfterAsync(before, 100));
            Assert.Equal((ChangeEntityType.ShelfGrant, ChangeAction.Updated), (entry.EntityType, entry.Action));
        }

        [Fact]
        public async Task SetAsync_SamePermission_LogsNothing()
        {
            Shelf shelf = await CreateShelfAsync();
            User user = await CreateUserAsync("user@example.com");
            await GrantService.SetAsync(shelf, user.Id, ShelfAccessPermission.Read);
            long before = await ChangeLog.GetLatestSequenceAsync();

            await GrantService.SetAsync(shelf, user.Id, ShelfAccessPermission.Read);

            Assert.Empty(await ChangeLog.GetAfterAsync(before, 100));
        }

        [Fact]
        public async Task SetAsync_UnknownUser_ThrowsNotFound()
        {
            Shelf shelf = await CreateShelfAsync();

            await Assert.ThrowsAsync<NotFoundException>(() => GrantService.SetAsync(shelf, Guid.NewGuid(), ShelfAccessPermission.Read));
        }

        [Fact]
        public async Task SetAsync_ShelfDeletedMeanwhile_ThrowsNotFound()
        {
            Shelf shelf = await CreateShelfAsync();
            User user = await CreateUserAsync("user@example.com");
            await ShelfService.DeleteAsync(shelf);

            await Assert.ThrowsAsync<NotFoundException>(() => GrantService.SetAsync(shelf, user.Id, ShelfAccessPermission.Read));
        }

        [Fact]
        public async Task SetAsync_UndefinedPermission_ThrowsArgumentException()
        {
            Shelf shelf = await CreateShelfAsync();
            User user = await CreateUserAsync("user@example.com");

            await Assert.ThrowsAsync<ArgumentException>(() => GrantService.SetAsync(shelf, user.Id, (ShelfAccessPermission)42));
        }

        [Fact]
        public async Task RemoveAsync_Grant_TakesTheAccessAwayAndLogsItGone()
        {
            Shelf shelf = await CreateShelfAsync();
            User user = await CreateUserAsync("user@example.com");
            await GrantService.SetAsync(shelf, user.Id, ShelfAccessPermission.Read);
            long before = await ChangeLog.GetLatestSequenceAsync();

            await GrantService.RemoveAsync(shelf, user.Id);

            Assert.Empty(await GrantService.GetForShelfAsync(shelf));
            ChangeLogEntry entry = Assert.Single(await ChangeLog.GetAfterAsync(before, 100));
            Assert.Equal((ChangeAction.Purged, (string?)null), (entry.Action, entry.Payload));
        }

        [Fact]
        public async Task RemoveAsync_NoGrant_ThrowsNotFound()
        {
            Shelf shelf = await CreateShelfAsync();
            User user = await CreateUserAsync("user@example.com");

            await Assert.ThrowsAsync<NotFoundException>(() => GrantService.RemoveAsync(shelf, user.Id));
        }
    }
}
