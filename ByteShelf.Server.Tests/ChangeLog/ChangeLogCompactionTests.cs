using ByteShelf.Server.ChangeLog.Models;
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

            Clock.Advance(ChangeLogRetention.Entries + TimeSpan.FromDays(1));
            await CompactChangeLogAsync();

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

            Clock.Advance(ChangeLogRetention.Entries + TimeSpan.FromDays(1));
            await CompactChangeLogAsync();

            Assert.Equal(added, await ChangeLog.GetPrunedThroughAsync());
        }

        [Fact]
        public async Task CompactAsync_EntriesWithinTheRetention_AreAllKept()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");
            await FileService.DeleteFileAsync(shelf, EntryReference.ForId(file.Id));
            int before = (await ChangeLog.GetAfterAsync(0, 100)).Count;

            Clock.Advance(ChangeLogRetention.Entries - TimeSpan.FromDays(1));
            await CompactChangeLogAsync();

            Assert.Equal(before, (await ChangeLog.GetAfterAsync(0, 100)).Count);
            Assert.Equal(0, await ChangeLog.GetPrunedThroughAsync());
        }

        [Fact]
        public async Task CompactAsync_PlainStatesOfAFileEncryptedLessThanADayAgo_AreKeptHoweverOld()
        {
            Shelf shelf = await CreateShelfAsync();
            await StoreMovedAndEncryptedTenDaysLaterAsync(shelf);
            int before = (await ChangeLog.GetAfterAsync(0, 1000)).Count;

            Clock.Advance(ChangeLogRetention.PlainTraces - TimeSpan.FromMinutes(1));
            await CompactChangeLogAsync();

            Assert.Equal(before, (await ChangeLog.GetAfterAsync(0, 1000)).Count);
            Assert.Equal(0, await ChangeLog.GetPrunedThroughAsync());
        }

        [Fact]
        public async Task CompactAsync_PlainStatesOfAFileEncryptedADayAgo_Go()
        {
            Shelf shelf = await CreateShelfAsync();
            (ShelfFile file, long moved) = await StoreMovedAndEncryptedTenDaysLaterAsync(shelf);

            Clock.Advance(ChangeLogRetention.PlainTraces + TimeSpan.FromMinutes(1));
            await CompactChangeLogAsync();

            IReadOnlyList<ChangeLogEntry> kept = await ChangeLog.GetAfterAsync(0, 1000);
            ChangeLogEntry state = Assert.Single(kept, entry => entry.EntityId == file.Id);
            Assert.True(ChangePayload.Deserialize<FileRecord>(state.Payload!).Encrypted);
            Assert.DoesNotContain(kept, entry => entry.Payload?.Contains(file.Sha256, StringComparison.Ordinal) == true);
            Assert.Equal(moved, await ChangeLog.GetPrunedThroughAsync());
        }

        [Fact]
        public async Task CompactAsync_AnotherFileWithTheSamePlainContents_KeepsItsStatesForTheRetention()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile other = await StoreTextAsync(shelf, "/other.txt", "the same contents");
            await FileService.MoveFileAsync(shelf, EntryReference.ForId(other.Id), "/moved.txt");
            ShelfFile encrypted = await StoreTextAsync(shelf, "/encrypted.txt", "the same contents");
            await EncryptInPlaceAsync(shelf, encrypted);

            Clock.Advance(ChangeLogRetention.PlainTraces + TimeSpan.FromHours(1));
            await CompactChangeLogAsync();

            IReadOnlyList<ChangeLogEntry> kept = await ChangeLog.GetAfterAsync(0, 1000);
            Assert.Equal(new[] { ChangeAction.Added, ChangeAction.Moved }, kept.Where(entry => entry.EntityId == other.Id).Select(entry => entry.Action));
            Assert.Equal(ChangeAction.Swapped, Assert.Single(kept, entry => entry.EntityId == encrypted.Id).Action);
        }

        [Fact]
        public async Task CompactAsync_WhatWasReadFromContentsReleasedLessThanADayAgo_IsKept()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile photo = await StoreReadPhotoAsync(shelf, "Known camera");
            await EncryptInPlaceAsync(shelf, photo);
            IReadOnlyList<long> read = (await ChangeLog.GetAfterAsync(0, 1000)).Where(IsReadFromContents).Select(entry => entry.Sequence).ToList();

            Clock.Advance(ChangeLogRetention.PlainTraces - TimeSpan.FromMinutes(1));
            await CompactChangeLogAsync();

            Assert.Equal(read, (await ChangeLog.GetAfterAsync(0, 1000)).Where(IsReadFromContents).Select(entry => entry.Sequence));
            Assert.Equal(0, await ChangeLog.GetPrunedThroughAsync());
        }

        [Fact]
        public async Task CompactAsync_WhatWasReadFromContentsReleasedADayAgo_GoesWithItsForgottenStates()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile photo = await StoreReadPhotoAsync(shelf, "Known camera");
            await EncryptInPlaceAsync(shelf, photo);
            IReadOnlyList<ChangeLogEntry> read = (await ChangeLog.GetAfterAsync(0, 1000)).Where(IsReadFromContents).ToList();
            // Each reader's entity: its states forgotten, then its release at the swap.
            Assert.All(read.GroupBy(entry => entry.EntityId), entity => Assert.Equal(ChangeAction.Released, entity.Last().Action));
            Assert.All(read, entry => Assert.Null(entry.Payload));

            Clock.Advance(ChangeLogRetention.PlainTraces + TimeSpan.FromMinutes(1));
            await CompactChangeLogAsync();

            Assert.DoesNotContain(await ChangeLog.GetAfterAsync(0, 1000), IsReadFromContents);
            // The file's plain state goes too, and comes before them; its encrypted state, after them, stays.
            Assert.Equal(read.Max(entry => entry.Sequence), await ChangeLog.GetPrunedThroughAsync());
        }

        [Fact]
        public async Task CompactAsync_WhatWasReadFromContentsPurgedADayAgo_IsKeptForTheRetention()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile photo = await StoreReadPhotoAsync(shelf, "Known camera");
            await FileService.DeleteFileAsync(shelf, EntryReference.ForId(photo.Id));
            await TrashService.EmptyAsync(shelf, CancellationToken.None);
            await Services.ReadMetadataAsync();
            IReadOnlyList<ChangeLogEntry> read = (await ChangeLog.GetAfterAsync(0, 1000)).Where(IsReadFromContents).ToList();
            Assert.All(read.GroupBy(entry => entry.EntityId), entity => Assert.Equal(ChangeAction.Purged, entity.Last().Action));

            Clock.Advance(ChangeLogRetention.PlainTraces + TimeSpan.FromHours(1));
            await CompactChangeLogAsync();

            Assert.Equal(read.Select(entry => entry.Sequence), (await ChangeLog.GetAfterAsync(0, 1000)).Where(IsReadFromContents).Select(entry => entry.Sequence));
            Assert.Equal(0, await ChangeLog.GetPrunedThroughAsync());
        }

        [Fact]
        public async Task CompactAsync_ADayAfterAnEncryption_KeepsTheNewestStateOfEverythingThatExists()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile plain = await StoreAsync(shelf, "/kept.jpg", TestMedia.Photo(make: "Kept camera"), "image/jpeg");
            await Services.ReadMetadataAsync();
            ShelfFile encrypted = await StoreTextAsync(shelf, "/encrypted.txt", "encrypted since");
            await EncryptInPlaceAsync(shelf, encrypted);
            ShelfFile decrypted = await StoreTextAsync(shelf, "/decrypted.txt", "encrypted and decrypted since");
            await EncryptInPlaceAsync(shelf, decrypted);
            await DecryptInPlaceAsync(shelf, decrypted.Id, "decrypted again");
            IReadOnlyList<ChangeLogEntry> newest = await Services.GetLatestStatesAsync();

            Clock.Advance(ChangeLogRetention.PlainTraces + TimeSpan.FromHours(1));
            await CompactChangeLogAsync();

            Assert.Equal(newest.Select(entry => entry.Sequence), (await Services.GetLatestStatesAsync()).Select(entry => entry.Sequence));
            Assert.Contains(newest, entry => entry.EntityType == ChangeEntityType.ObjectMetadata && entry.Payload?.Contains(plain.Sha256, StringComparison.Ordinal) == true);
            Assert.False(ChangePayload.Deserialize<FileRecord>(newest.Single(entry => entry.EntityId == decrypted.Id).Payload!).Encrypted);
            Assert.True(ChangePayload.Deserialize<FileRecord>(newest.Single(entry => entry.EntityId == encrypted.Id).Payload!).Encrypted);
        }

        [Fact]
        public async Task GetLatestStatesPageAsync_AnEntityChangedTwice_AppearsOnceAtItsNewest()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");
            await FileService.MoveFileAsync(shelf, EntryReference.ForId(file.Id), "/b.txt");

            IReadOnlyList<ChangeLogEntry> states = await Services.GetLatestStatesAsync();

            ChangeLogEntry fileState = Assert.Single(states, entry => entry.EntityId == file.Id);
            Assert.Equal(ChangeAction.Moved, fileState.Action);
        }

        [Fact]
        public async Task GetLatestStatesPageAsync_AnEntityRemoved_ComesAsItsRemoval()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");
            await FileService.DeleteFileAsync(shelf, EntryReference.ForId(file.Id));
            await TrashService.EmptyAsync(shelf, CancellationToken.None);

            IReadOnlyList<ChangeLogEntry> states = await Services.GetLatestStatesAsync();

            // So a copy that took the file on an earlier page drops it.
            ChangeLogEntry removal = Assert.Single(states, entry => entry.EntityId == file.Id);
            Assert.Equal((ChangeAction.Purged, null), (removal.Action, removal.Payload));
        }

        private static bool IsReadFromContents(ChangeLogEntry entry)
        {
            return ChangeEntityType.ReadFromContents.Contains(entry.EntityType);
        }

        /// <summary>
        /// Stores a file, moves it, and encrypts it in place ten days later, so its plain states are older than the
        /// day it is kept after; answers it and the sequence of its last plain state.
        /// </summary>
        private async Task<(ShelfFile File, long Moved)> StoreMovedAndEncryptedTenDaysLaterAsync(Shelf shelf)
        {
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "known plain contents");
            await FileService.MoveFileAsync(shelf, EntryReference.ForId(file.Id), "/b.txt");
            long moved = (await ChangeLog.GetAfterAsync(0, 1000)).Last(entry => entry.EntityId == file.Id).Sequence;
            Clock.Advance(TimeSpan.FromDays(10));
            await EncryptInPlaceAsync(shelf, file);
            return (file, moved);
        }
    }
}
