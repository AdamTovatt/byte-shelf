using ByteShelf.Server.ChangeLog.Models;
using ByteShelf.Server.ChangeLog.Repositories;
using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;
using Microsoft.Extensions.DependencyInjection;

namespace ByteShelf.Server.Tests.Storage
{
    /// <summary>The change log records every storage change, in commit order, with the entity's state afterwards.</summary>
    public class ChangeLogTests : ServiceTestBase
    {
        private IChangeLogRepository ChangeLog => Services.GetRequiredService<IChangeLogRepository>();

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
    }
}
