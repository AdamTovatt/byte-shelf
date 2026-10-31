using ByteShelf.Server.ChangeLog.Models;
using ByteShelf.Server.ChangeLog.Services;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;
using EasyReasy.Database;
using Microsoft.Extensions.DependencyInjection;

namespace ByteShelf.Server.Tests.ChangeLog
{
    /// <summary>Compaction keeps the newest state of everything that exists, and remembers how far it dropped entries.</summary>
    public class ChangeLogCompactionTests : ServiceTestBase
    {
        [Fact]
        public async Task CompactAsync_OldEntries_KeepsOnlyTheNewestStateOfWhatExists()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile moved = await StoreTextAsync(shelf, "/a.txt", "a");
            await FileService.MoveFileAsync(shelf, EntryReference.ForId(moved.Id), "/b.txt");
            ShelfFile purged = await StoreTextAsync(shelf, "/c.txt", "c");
            await FileService.DeleteFileAsync(shelf, EntryReference.ForId(purged.Id));
            await TrashService.EmptyAsync(shelf, CancellationToken.None);
            long latest = await ChangeLog.GetLatestSequenceAsync();
            long removal = (await ChangeLog.GetAfterAsync(0, 100)).Last(entry => entry.EntityId == purged.Id).Sequence;

            Clock.Advance(ChangeLogCompactionWorker.Retention + TimeSpan.FromDays(1));
            await CompactAsync();

            IReadOnlyList<ChangeLogEntry> kept = await ChangeLog.GetAfterAsync(0, 100);
            Assert.Equal(
                new[] { (ChangeEntityType.Shelf, ChangeAction.Added), (ChangeEntityType.Directory, ChangeAction.Added), (ChangeEntityType.File, ChangeAction.Moved) },
                kept.Select(entry => (entry.EntityType, entry.Action)));
            Assert.Equal(latest, await ChangeLog.GetLatestSequenceAsync());
            Assert.Equal(removal, await ChangeLog.GetPrunedThroughAsync());
        }

        [Fact]
        public async Task CompactAsync_OnlySupersededEntries_RemembersTheHighestDropped()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");
            long added = (await ChangeLog.GetAfterAsync(0, 100)).Single(entry => entry.EntityId == file.Id).Sequence;
            await FileService.MoveFileAsync(shelf, EntryReference.ForId(file.Id), "/b.txt");

            Clock.Advance(ChangeLogCompactionWorker.Retention + TimeSpan.FromDays(1));
            await CompactAsync();

            Assert.Equal(added, await ChangeLog.GetPrunedThroughAsync());
        }

        [Fact]
        public async Task CompactAsync_EntriesWithinTheRetention_AreAllKept()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");
            await FileService.DeleteFileAsync(shelf, EntryReference.ForId(file.Id));
            int before = (await ChangeLog.GetAfterAsync(0, 100)).Count;

            Clock.Advance(ChangeLogCompactionWorker.Retention - TimeSpan.FromDays(1));
            await CompactAsync();

            Assert.Equal(before, (await ChangeLog.GetAfterAsync(0, 100)).Count);
            Assert.Equal(0, await ChangeLog.GetPrunedThroughAsync());
        }

        [Fact]
        public async Task GetLatestStatesAfterAsync_AnEntityChangedTwice_AppearsOnceAtItsNewest()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");
            await FileService.MoveFileAsync(shelf, EntryReference.ForId(file.Id), "/b.txt");

            IReadOnlyList<ChangeLogEntry> states = await ChangeLog.GetLatestStatesAfterAsync(0, 100);

            ChangeLogEntry fileState = Assert.Single(states, entry => entry.EntityId == file.Id);
            Assert.Equal(ChangeAction.Moved, fileState.Action);
        }

        private async Task CompactAsync()
        {
            await using IDbTransactionSession session = await Services.GetRequiredService<IDbSessionFactory>().CreateSessionWithTransactionAsync();
            await ChangeLog.CompactAsync(Clock.GetUtcNow() - ChangeLogCompactionWorker.Retention, session);
            await session.CommitAsync();
        }
    }
}
