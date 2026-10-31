using ByteShelf.Server.ChangeLog.Models;
using ByteShelf.Server.Shared.Auth;
using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelf.Server.Users.Models;
using ByteShelf.Server.Users.Repositories;
using ByteShelf.Server.Users.Services;
using ByteShelfCommon;
using Microsoft.Extensions.DependencyInjection;

namespace ByteShelf.Server.Tests.ChangeLog
{
    /// <summary>
    /// The change log records every change to storage, users and access, in commit order, with the
    /// entity's state afterwards.
    /// </summary>
    public class ChangeLogTests : ServiceTestBase
    {
        [Fact]
        public async Task StoreAsync_NewDirectory_LogsDirectoryThenFile()
        {
            Shelf shelf = await CreateShelfAsync();
            long before = await ChangeLog.GetLatestSequenceAsync();

            ShelfFile file = await StoreTextAsync(shelf, "/dir/a.txt", "a");

            IReadOnlyList<ChangeLogEntry> entries = await ChangeLog.GetAfterAsync(before, 100);
            Assert.Equal(
                new[] { (ChangeEntityType.Directory, ChangeAction.Added), (ChangeEntityType.File, ChangeAction.Added) },
                entries.Select(entry => (entry.EntityType, entry.Action)));
            FileRecord logged = ChangePayload.Deserialize<FileRecord>(entries[1].Payload!);
            Assert.Equal(file.Id, logged.Id);
            Assert.Equal(file.Sha256, logged.Sha256);
        }

        [Fact]
        public async Task StoreAsync_Overwrite_LogsOldFileDeletedThenNewFileAdded()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile old = await StoreTextAsync(shelf, "/a.txt", "old");
            long before = await ChangeLog.GetLatestSequenceAsync();

            await StoreTextAsync(shelf, "/a.txt", "new", overwrite: true);

            IReadOnlyList<ChangeLogEntry> entries = await ChangeLog.GetAfterAsync(before, 100);
            Assert.Equal(new[] { ChangeAction.Deleted, ChangeAction.Added }, entries.Select(entry => entry.Action));
            FileRecord deleted = ChangePayload.Deserialize<FileRecord>(entries[0].Payload!);
            Assert.Equal(old.Id, deleted.Id);
            Assert.NotNull(deleted.DeletedAt);
        }

        [Fact]
        public async Task PurgeExpiredAsync_LogsPurgeWithoutPayload()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");
            await FileService.DeleteFileAsync(shelf, EntryReference.ForId(file.Id));
            Clock.Advance(Options.RestoreWindow + TimeSpan.FromSeconds(1));
            long before = await ChangeLog.GetLatestSequenceAsync();

            await TrashService.PurgeExpiredAsync(CancellationToken.None);

            ChangeLogEntry entry = Assert.Single(await ChangeLog.GetAfterAsync(before, 100));
            Assert.Equal(ChangeAction.Purged, entry.Action);
            Assert.Equal(file.Id, entry.EntityId);
            Assert.Null(entry.Payload);
        }

        [Fact]
        public async Task StoreAsync_RefusedForConflict_LogsNothing()
        {
            Shelf shelf = await CreateShelfAsync();
            await StoreTextAsync(shelf, "/a.txt", "a");
            long before = await ChangeLog.GetLatestSequenceAsync();

            await Assert.ThrowsAsync<ConflictException>(() => StoreTextAsync(shelf, "/a.txt", "b"));

            Assert.Empty(await ChangeLog.GetAfterAsync(before, 100));
        }

        [Fact]
        public async Task RestoreAsync_LogsEachRestoredRowWithItsLiveState()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");
            TrashEntry deletion = await FileService.DeleteFileAsync(shelf, EntryReference.ForId(file.Id));
            long before = await ChangeLog.GetLatestSequenceAsync();

            await TrashService.RestoreAsync(shelf, deletion.DeletionId);

            ChangeLogEntry entry = Assert.Single(await ChangeLog.GetAfterAsync(before, 100));
            Assert.Equal(ChangeAction.Restored, entry.Action);
            FileRecord restored = ChangePayload.Deserialize<FileRecord>(entry.Payload!);
            Assert.Equal(file.Id, restored.Id);
            Assert.Null(restored.DeletedAt);
            Assert.Null(restored.DeletionId);
        }

        [Fact]
        public async Task MoveFileAsync_LogsTheFileMovedWithItsNewPlace()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");
            long before = await ChangeLog.GetLatestSequenceAsync();

            ShelfFile moved = await FileService.MoveFileAsync(shelf, EntryReference.ForId(file.Id), "/renamed.txt");

            ChangeLogEntry entry = Assert.Single(await ChangeLog.GetAfterAsync(before, 100));
            Assert.Equal((ChangeEntityType.File, ChangeAction.Moved), (entry.EntityType, entry.Action));
            Assert.Equal("renamed.txt", ChangePayload.Deserialize<FileRecord>(entry.Payload!).Name);
            Assert.Equal(moved.DirectoryId, ChangePayload.Deserialize<FileRecord>(entry.Payload!).DirectoryId);
        }

        [Fact]
        public async Task MoveDirectoryAsync_LogsOnlyTheDirectoryMovedWithItsNewPlace()
        {
            Shelf shelf = await CreateShelfAsync();
            await StoreTextAsync(shelf, "/drafts/a.txt", "a");
            ShelfDirectory archive = await DirectoryService.CreateAsync(shelf, "/archive");
            long before = await ChangeLog.GetLatestSequenceAsync();

            ShelfDirectory moved = await DirectoryService.MoveAsync(shelf, EntryReference.ForPath("/drafts"), "/archive/old");

            ChangeLogEntry entry = Assert.Single(await ChangeLog.GetAfterAsync(before, 100));
            Assert.Equal((ChangeEntityType.Directory, ChangeAction.Moved, moved.Id), (entry.EntityType, entry.Action, entry.EntityId));
            DirectoryRecord logged = ChangePayload.Deserialize<DirectoryRecord>(entry.Payload!);
            Assert.Equal(("old", archive.Id), (logged.Name, logged.ParentId));
        }

        [Fact]
        public async Task DeleteDirectoryAsync_LogsEveryDirectoryThenEveryFileDeleted()
        {
            Shelf shelf = await CreateShelfAsync();
            await StoreTextAsync(shelf, "/photos/2026/a.jpg", "a");
            long before = await ChangeLog.GetLatestSequenceAsync();

            await DirectoryService.DeleteAsync(shelf, EntryReference.ForPath("/photos"));

            IReadOnlyList<ChangeLogEntry> entries = await ChangeLog.GetAfterAsync(before, 100);
            Assert.Equal(
                new[] { ChangeEntityType.Directory, ChangeEntityType.Directory, ChangeEntityType.File },
                entries.Select(entry => entry.EntityType));
            Assert.All(entries, entry => Assert.Equal(ChangeAction.Deleted, entry.Action));
        }

        [Fact]
        public async Task DeleteAsync_Shelf_LogsItsRootThenItselfGone()
        {
            Shelf shelf = await CreateShelfAsync();
            long before = await ChangeLog.GetLatestSequenceAsync();

            await ShelfService.DeleteAsync(shelf);

            IReadOnlyList<ChangeLogEntry> entries = await ChangeLog.GetAfterAsync(before, 100);
            Assert.Equal(
                new[] { (ChangeEntityType.Directory, ChangeAction.Purged), (ChangeEntityType.Shelf, ChangeAction.Purged) },
                entries.Select(entry => (entry.EntityType, entry.Action)));
            Assert.All(entries, entry => Assert.Null(entry.Payload));
        }

        [Fact]
        public async Task UpdateAsync_Shelf_LogsItUpdatedWithItsNewSettings()
        {
            Shelf shelf = await CreateShelfAsync();
            long before = await ChangeLog.GetLatestSequenceAsync();

            await ShelfService.UpdateAsync(shelf, "renamed", 4096);

            ChangeLogEntry entry = Assert.Single(await ChangeLog.GetAfterAsync(before, 100));
            Assert.Equal((ChangeEntityType.Shelf, ChangeAction.Updated, shelf.Id), (entry.EntityType, entry.Action, entry.EntityId));
            Shelf logged = ChangePayload.Deserialize<Shelf>(entry.Payload!);
            Assert.Equal(("renamed", 4096L), (logged.Name, logged.QuotaBytes));
        }

        [Fact]
        public async Task UpdateAsync_ShelfDeletedMeanwhile_ThrowsNotFoundAndLogsNothing()
        {
            Shelf shelf = await CreateShelfAsync();
            await ShelfService.DeleteAsync(shelf);
            long before = await ChangeLog.GetLatestSequenceAsync();

            await Assert.ThrowsAsync<NotFoundException>(() => ShelfService.UpdateAsync(shelf, "renamed", null));

            Assert.Empty(await ChangeLog.GetAfterAsync(before, 100));
        }

        [Fact]
        public async Task EmptyAsync_LogsEveryTrashedRowPurged()
        {
            Shelf shelf = await CreateShelfAsync();
            await StoreTextAsync(shelf, "/photos/a.jpg", "a");
            await DirectoryService.DeleteAsync(shelf, EntryReference.ForPath("/photos"));
            long before = await ChangeLog.GetLatestSequenceAsync();

            await TrashService.EmptyAsync(shelf, CancellationToken.None);

            IReadOnlyList<ChangeLogEntry> entries = await ChangeLog.GetAfterAsync(before, 100);
            Assert.Equal(
                new[] { (ChangeEntityType.File, ChangeAction.Purged), (ChangeEntityType.Directory, ChangeAction.Purged) },
                entries.Select(entry => (entry.EntityType, entry.Action)));
        }

        [Fact]
        public async Task CreateUserAsync_LogsTheUserThenTheirLoginWithItsPasswordHash()
        {
            long before = await ChangeLog.GetLatestSequenceAsync();

            User user = await CreateUserAsync("user@example.com");

            IReadOnlyList<ChangeLogEntry> entries = await ChangeLog.GetAfterAsync(before, 100);
            Assert.Equal(
                new[] { (ChangeEntityType.User, ChangeAction.Added), (ChangeEntityType.UserLogin, ChangeAction.Added) },
                entries.Select(entry => (entry.EntityType, entry.Action)));
            Assert.Equal("user@example.com", ChangePayload.Deserialize<User>(entries[0].Payload!).Email);
            UserLogin login = ChangePayload.Deserialize<UserLogin>(entries[1].Payload!);
            Assert.Equal(user.Id, login.UserId);
            Assert.Equal(await StoredPasswordHashAsync("user@example.com"), login.Credential);
        }

        [Fact]
        public async Task UpdatePasswordAsync_LogsTheLoginWithTheNewHash()
        {
            User user = await CreateUserAsync("user@example.com");
            long before = await ChangeLog.GetLatestSequenceAsync();

            await Services.GetRequiredService<IUserService>().UpdatePasswordAsync(user.Id, "password-for-tests", "a-new-password");

            ChangeLogEntry entry = Assert.Single(await ChangeLog.GetAfterAsync(before, 100));
            Assert.Equal((ChangeEntityType.UserLogin, ChangeAction.Updated), (entry.EntityType, entry.Action));
            Assert.Equal(await StoredPasswordHashAsync("user@example.com"), ChangePayload.Deserialize<UserLogin>(entry.Payload!).Credential);
        }

        [Fact]
        public async Task UpdateRolesAsync_LogsTheUserWithTheNewRoles()
        {
            User user = await CreateUserAsync("user@example.com");
            long before = await ChangeLog.GetLatestSequenceAsync();

            await Services.GetRequiredService<IUserService>().UpdateRolesAsync(user.Id, new[] { UserRoles.Admin });

            ChangeLogEntry entry = Assert.Single(await ChangeLog.GetAfterAsync(before, 100));
            Assert.Equal((ChangeEntityType.User, ChangeAction.Updated), (entry.EntityType, entry.Action));
            Assert.Equal(new[] { UserRoles.Admin }, ChangePayload.Deserialize<User>(entry.Payload!).Roles);
        }

        [Fact]
        public async Task DeleteUserAsync_LogsTheirAccessAndLoginGoneBeforeThem()
        {
            Shelf shelf = await CreateShelfAsync();
            User user = await CreateUserAsync("user@example.com");
            await GrantService.SetAsync(shelf, user.Id, ShelfAccessPermission.Read);
            long before = await ChangeLog.GetLatestSequenceAsync();

            await Services.GetRequiredService<IUserService>().DeleteUserAsync(user.Id);

            IReadOnlyList<ChangeLogEntry> entries = await ChangeLog.GetAfterAsync(before, 100);
            Assert.Equal(
                new[] { ChangeEntityType.ShelfGrant, ChangeEntityType.UserLogin, ChangeEntityType.User },
                entries.Select(entry => entry.EntityType));
            Assert.All(entries, entry => Assert.Equal(ChangeAction.Purged, entry.Action));
        }

        [Fact]
        public async Task DeleteAsync_ShelfWithAGrantAndAKey_LogsThemGoneBeforeTheShelf()
        {
            Shelf shelf = await CreateShelfAsync();
            User user = await CreateUserAsync("user@example.com");
            await GrantService.SetAsync(shelf, user.Id, ShelfAccessPermission.Read);
            await KeyService.CreateAsync(shelf, "app", ShelfKeyPermission.Read);
            long before = await ChangeLog.GetLatestSequenceAsync();

            await ShelfService.DeleteAsync(shelf);

            IReadOnlyList<ChangeLogEntry> entries = await ChangeLog.GetAfterAsync(before, 100);
            Assert.Equal(
                new[] { ChangeEntityType.ShelfGrant, ChangeEntityType.ShelfKey, ChangeEntityType.Directory, ChangeEntityType.Shelf },
                entries.Select(entry => entry.EntityType));
            Assert.All(entries, entry => Assert.Equal(ChangeAction.Purged, entry.Action));
        }

        [Fact]
        public async Task CreateAsync_Shelf_LogsTheShelfThenItsRoot()
        {
            long before = await ChangeLog.GetLatestSequenceAsync();

            Shelf shelf = await CreateShelfAsync();

            IReadOnlyList<ChangeLogEntry> entries = await ChangeLog.GetAfterAsync(before, 100);
            Assert.Equal(
                new[] { (ChangeEntityType.Shelf, shelf.Id), (ChangeEntityType.Directory, entries[1].EntityId) },
                entries.Select(entry => (entry.EntityType, entry.EntityId)));
            Assert.Equal(TestInstants.Now, entries[0].CreatedAt);
        }

        private async Task<string?> StoredPasswordHashAsync(string email)
        {
            return (await Services.GetRequiredService<IUserLoginRepository>().GetCredentialsByEmailAsync(email))?.PasswordHash;
        }
    }
}
