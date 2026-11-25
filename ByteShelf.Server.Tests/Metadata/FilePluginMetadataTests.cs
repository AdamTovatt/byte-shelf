using System.Data.Common;
using ByteShelf.Server.ChangeLog.Models;
using ByteShelf.Server.Metadata.Models;
using ByteShelf.Server.Metadata.Repositories;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;
using Microsoft.Extensions.DependencyInjection;

namespace ByteShelf.Server.Tests.Metadata
{
    /// <summary>What plugins keep about a file: it is the file's, as its description is, and goes with its row.</summary>
    public class FilePluginMetadataTests : ServiceTestBase
    {
        [Fact]
        public async Task SetAsync_FirstWriteAndTheNext_CountVersionsAndLogTheState()
        {
            ShelfFile file = await StoreAFileAsync();
            long before = await ChangeLog.GetLatestSequenceAsync();

            FilePluginMetadata? first = await SetAsync(file.Id, null, "{\"rating\":4}");
            FilePluginMetadata? second = await SetAsync(file.Id, 1, "{\"rating\":5}");

            Assert.Equal((1, 2), (first?.Version, second?.Version));
            FilePluginMetadata kept = await GetAsync(file.Id);
            Assert.Equal((2, "{\"rating\":5}"), (kept.Version, kept.Document?.Json));
            IReadOnlyList<ChangeLogEntry> logged = await ChangeLog.GetAfterAsync(before, 100);
            Assert.Equal(
                [(ChangeEntityType.FilePluginMetadata, ChangeAction.Added), (ChangeEntityType.FilePluginMetadata, ChangeAction.Updated)],
                logged.Select(entry => (entry.EntityType, entry.Action)));
            FilePluginMetadata loggedLast = ChangePayload.Deserialize<FilePluginMetadata>(logged[1].Payload!);
            Assert.Equal((kept.Version, kept.Document), (loggedLast.Version, loggedLast.Document));
        }

        [Theory]
        [InlineData(null)]
        [InlineData(2)]
        public async Task SetAsync_AnotherVersionIsThere_ChangesAndLogsNothing(int? expectedVersion)
        {
            ShelfFile file = await StoreAFileAsync();
            await SetAsync(file.Id, null, "{\"rating\":4}");
            long before = await ChangeLog.GetLatestSequenceAsync();

            Assert.Null(await SetAsync(file.Id, expectedVersion, "{}"));

            Assert.Equal("{\"rating\":4}", await Services.GetFileDocumentAsync(file.Id));
            Assert.Equal(before, await ChangeLog.GetLatestSequenceAsync());
        }

        [Fact]
        public async Task SetAsync_NoDocument_RemovesItAndKeepsAndLogsItsVersion()
        {
            ShelfFile file = await StoreAFileAsync();
            await SetAsync(file.Id, null, "{}");

            FilePluginMetadata? removed = await SetAsync(file.Id, 1, null);

            Assert.Equal((2, null), (removed?.Version, removed?.Document));
            FilePluginMetadata kept = await GetAsync(file.Id);
            Assert.Equal((2, null), (kept.Version, kept.Document));
            ChangeLogEntry logged = (await ChangeLog.GetAfterAsync(0, 100)).Last(entry => entry.EntityId == kept.Id);
            Assert.Equal((2, null), (ChangePayload.Deserialize<FilePluginMetadata>(logged.Payload!).Version, ChangePayload.Deserialize<FilePluginMetadata>(logged.Payload!).Document));
        }

        [Fact]
        public async Task SetAsync_NoDocumentOverAnotherVersion_RemovesNothing()
        {
            ShelfFile file = await StoreAFileAsync();
            await SetAsync(file.Id, null, "{\"rating\":4}");

            Assert.Null(await SetAsync(file.Id, 2, null));

            Assert.Equal("{\"rating\":4}", await Services.GetFileDocumentAsync(file.Id));
        }

        /// <summary>A removal that let versions start over would give the second document the number a writer of the first still holds.</summary>
        [Fact]
        public async Task SetAsync_OverAVersionRemovedAndWrittenAnewSince_IsRefused()
        {
            ShelfFile file = await StoreAFileAsync();
            await SetAsync(file.Id, null, "{\"rating\":1}");
            await SetAsync(file.Id, 1, null);
            Assert.Null(await SetAsync(file.Id, null, "{\"rating\":0}"));
            Assert.Equal(3, (await SetAsync(file.Id, 2, "{\"rating\":2}"))?.Version);

            Assert.Null(await SetAsync(file.Id, 1, "{\"rating\":3}"));

            Assert.Equal("{\"rating\":2}", await Services.GetFileDocumentAsync(file.Id));
        }

        [Fact]
        public async Task SetAsync_NoSuchFile_IsRefusedByTheDatabase()
        {
            await Assert.ThrowsAnyAsync<DbException>(() => SetAsync(Guid.NewGuid(), null, "{}"));
        }

        [Fact]
        public async Task StoreAsync_OverAFile_KeepsEveryPluginsDocumentOnTheNewOneAndOnTheOldInTheTrash()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile first = await StoreTextAsync(shelf, "/a.txt", "first");
            await SetAsync(first.Id, null, "{\"rating\":4}");
            await SetAsync(first.Id, 1, "{\"rating\":5}");
            await Services.SetFileDocumentAsync(first.Id, "{\"tags\":[\"x\"]}", "another-plugin");
            await Services.SetFileDocumentAsync(first.Id, "{}", "removed");
            await Services.RemoveFileDocumentAsync(first.Id, "removed");
            long before = await ChangeLog.GetLatestSequenceAsync();

            ShelfFile second = await StoreTextAsync(shelf, "/a.txt", "second", overwrite: true);

            FilePluginMetadata kept = await GetAsync(second.Id);
            Assert.Equal((2, "{\"rating\":5}"), (kept.Version, kept.Document?.Json));
            Assert.Equal("{\"tags\":[\"x\"]}", await Services.GetFileDocumentAsync(second.Id, "another-plugin"));
            Assert.Equal((2, null), ((await GetAsync(second.Id, "removed")).Version, (await GetAsync(second.Id, "removed")).Document));
            Assert.Equal("{\"rating\":5}", await Services.GetFileDocumentAsync(first.Id));
            Assert.Equal(
                new[] { "another-plugin", "removed", TestPluginMetadata.Plugin }.Select(plugin => FilePluginMetadata.IdFor(plugin, second.Id)),
                (await ChangeLog.GetAfterAsync(before, 100)).Where(entry => entry.EntityType == ChangeEntityType.FilePluginMetadata).Select(entry => entry.EntityId));
        }

        [Fact]
        public async Task SwapAsync_FileEncryptedInPlace_KeepsTheDocumentAndLogsItAfterTheFile()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "plain");
            FilePluginMetadata set = (await SetAsync(file.Id, null, "{\"rating\":4}"))!;
            long before = await ChangeLog.GetLatestSequenceAsync();

            await EncryptInPlaceAsync(shelf, file);

            FilePluginMetadata kept = await GetAsync(file.Id);
            Assert.Equal((1, "{\"rating\":4}"), (kept.Version, kept.Document?.Json));
            Assert.Equal([file.Id, set.Id], (await ChangeLog.GetAfterAsync(before, 100)).Select(entry => entry.EntityId));
        }

        [Fact]
        public async Task TrashService_FileDeletedAndRestored_KeepsTheDocument()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");
            await SetAsync(file.Id, null, "{\"rating\":4}");

            TrashEntry deleted = await FileService.DeleteFileAsync(shelf, EntryReference.ForId(file.Id));
            await TrashService.RestoreAsync(shelf, deleted.DeletionId);

            Assert.Equal("{\"rating\":4}", await Services.GetFileDocumentAsync(file.Id));
        }

        [Fact]
        public async Task TrashService_FilePurged_TakesTheDocumentUnlogged()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");
            FilePluginMetadata set = (await SetAsync(file.Id, null, "{\"rating\":4}"))!;

            await FileService.DeleteFileAsync(shelf, EntryReference.ForId(file.Id));
            await TrashService.EmptyAsync(shelf, CancellationToken.None);

            Assert.Null(await Rows.GetAsync(TestPluginMetadata.Plugin, file.Id));
            Assert.Single(await ChangeLog.GetAfterAsync(0, 100), entry => entry.EntityId == set.Id);
        }

        [Fact]
        public async Task CompactAsync_DocumentOfAPurgedFile_LeavesTheLogOnceOldAndOneOfAKeptFileStays()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile purged = await StoreTextAsync(shelf, "/a.txt", "a");
            ShelfFile kept = await StoreTextAsync(shelf, "/b.txt", "b");
            FilePluginMetadata gone = (await SetAsync(purged.Id, null, "{\"rating\":4}"))!;
            FilePluginMetadata stays = (await SetAsync(kept.Id, null, "{\"rating\":5}"))!;
            long goneAt = (await ChangeLog.GetAfterAsync(0, 100)).Single(entry => entry.EntityId == gone.Id).Sequence;
            await FileService.DeleteFileAsync(shelf, EntryReference.ForId(purged.Id));
            await TrashService.EmptyAsync(shelf, CancellationToken.None);

            await CompactChangeLogAsync();
            Assert.Contains(await ChangeLog.GetAfterAsync(0, 100), entry => entry.EntityId == gone.Id);

            Clock.Advance(ChangeLogRetention.Entries + TimeSpan.FromDays(1));
            await CompactChangeLogAsync();

            IReadOnlyList<ChangeLogEntry> left = await ChangeLog.GetAfterAsync(0, 100);
            Assert.DoesNotContain(left, entry => entry.EntityId == gone.Id);
            Assert.Contains(left, entry => entry.EntityId == stays.Id);
            Assert.True(await ChangeLog.GetPrunedThroughAsync() >= goneAt);
        }

        private IFilePluginMetadataRepository Rows => Services.GetRequiredService<IFilePluginMetadataRepository>();

        private async Task<ShelfFile> StoreAFileAsync()
        {
            return await StoreTextAsync(await CreateShelfAsync(), "/a.txt", "a");
        }

        private async Task<FilePluginMetadata> GetAsync(Guid fileId, string plugin = TestPluginMetadata.Plugin)
        {
            return (await Rows.GetAsync(plugin, fileId))!;
        }

        private Task<FilePluginMetadata?> SetAsync(Guid fileId, int? expectedVersion, string? json)
        {
            PluginDocument? document = json == null ? null : PluginDocument.Parse(json);
            return Services.InTransactionAsync((provider, session) => provider.GetRequiredService<IFilePluginMetadataRepository>()
                .SetAsync(TestPluginMetadata.Plugin, fileId, expectedVersion, document, Clock.GetUtcNow(), session));
        }
    }
}
