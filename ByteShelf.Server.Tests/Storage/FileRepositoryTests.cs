using ByteShelf.Server.ChangeLog.Models;
using ByteShelf.Server.Metadata.Models;
using ByteShelf.Server.Metadata.Repositories;
using ByteShelf.Server.Metadata.Services;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Repositories;
using ByteShelf.Server.Storage.Services;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;
using EasyReasy.Database;
using Microsoft.Extensions.DependencyInjection;

namespace ByteShelf.Server.Tests.Storage
{
    /// <summary>The file rows by object, which a backup reads and prunes.</summary>
    public sealed class FileRepositoryTests : ServiceTestBase
    {
        private IFileRepository Files => Services.GetRequiredService<IFileRepository>();

        [Fact]
        public async Task GetReferencedObjectsAsync_LiveAndTrashedFiles_ListsEachObjectOnceInOrder()
        {
            Shelf shelf = await CreateShelfAsync();
            await StoreTextAsync(shelf, "/a.txt", "shared");
            await StoreTextAsync(shelf, "/b.txt", "shared");
            await StoreTextAsync(shelf, "/trashed.txt", "own");
            await FileService.DeleteFileAsync(shelf, EntryReference.ForPath("/trashed.txt"));

            IReadOnlyList<string> objects = await Files.GetReferencedObjectsAsync();

            Assert.Equal(new[] { Sha256Of("shared"), Sha256Of("own") }.Order(StringComparer.Ordinal), objects);
        }

        [Fact]
        public async Task GetShelfIdsAsync_LiveTrashedAndUnknownFiles_AnswersThoseThereAre()
        {
            Shelf docs = await CreateShelfAsync();
            Shelf photos = await CreateShelfAsync("photos");
            ShelfFile live = await StoreTextAsync(docs, "/live.txt", "a");
            ShelfFile trashed = await StoreTextAsync(photos, "/trashed.txt", "a");
            await StoreTextAsync(photos, "/unasked.txt", "a");
            await FileService.DeleteFileAsync(photos, EntryReference.ForPath("/trashed.txt"));

            IReadOnlyDictionary<Guid, Guid> shelves = await Files.GetShelfIdsAsync([live.Id, trashed.Id, Guid.NewGuid()]);

            Assert.Equal(new Dictionary<Guid, Guid> { [live.Id] = docs.Id, [trashed.Id] = photos.Id }, shelves);
        }

        [Fact]
        public async Task GetShelfIdsAsync_OfNone_IsEmpty()
        {
            await StoreTextAsync(await CreateShelfAsync(), "/a.txt", "a");

            Assert.Empty(await Files.GetShelfIdsAsync([]));
        }

        [Fact]
        public async Task PurgeAllWithObjectAsync_LiveAndTrashedRows_RemovesAndLogsEach()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile live = await StoreTextAsync(shelf, "/live.txt", "gone");
            ShelfFile trashed = await StoreTextAsync(shelf, "/trashed.txt", "gone");
            ShelfFile other = await StoreTextAsync(shelf, "/other.txt", "kept");
            await FileService.DeleteFileAsync(shelf, EntryReference.ForPath("/trashed.txt"));
            long before = await ChangeLog.GetLatestSequenceAsync();

            int purged;
            await using (IDbTransactionSession session = await TestRoot.SessionFactory.CreateSessionWithTransactionAsync())
            {
                purged = await Files.PurgeAllWithObjectAsync(Sha256Of("gone"), TestInstants.Now, session);
                await session.CommitAsync();
            }

            Assert.Equal(2, purged);
            Assert.Null(await Files.GetByIdAsync(live.Id));
            Assert.Null(await Files.GetByIdAsync(trashed.Id));
            Assert.NotNull(await Files.GetByIdAsync(other.Id));
            IReadOnlyList<ChangeLogEntry> logged = await ChangeLog.GetAfterAsync(before, 100);
            Assert.Equal(
                new[] { live.Id, trashed.Id }.Order(),
                logged.Where(entry => entry.EntityType == ChangeEntityType.File && entry.Action == ChangeAction.Purged).Select(entry => entry.EntityId).Order());
        }

        [Fact]
        public async Task GetLiveWithObjectAsync_LiveAndTrashedRows_AnswersOnlyTheLiveOnes()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile live = await StoreTextAsync(shelf, "/live.txt", "shared");
            await StoreTextAsync(shelf, "/trashed.txt", "shared");
            await StoreTextAsync(shelf, "/other.txt", "other");
            await FileService.DeleteFileAsync(shelf, EntryReference.ForPath("/trashed.txt"));

            IReadOnlyList<FileRecord> files = await Files.GetLiveWithObjectAsync(Sha256Of("shared"));

            Assert.Equal(live.Id, Assert.Single(files).Id);
        }

        [Fact]
        public async Task SwapContentAsync_ContentsNoLongerTheExpectedOnes_ChangesAndLogsNothing()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "now");
            await Services.SetFileDocumentAsync(file.Id, "{}");
            long before = await ChangeLog.GetLatestSequenceAsync();

            FileRecord? swapped;
            await using (ContentsSession session = await Services.CreateContentsSessionAsync())
            {
                swapped = await Files.SwapContentAsync(file.Id, Sha256Of("then"), Sha256Of("new"), 3, encrypted: true, TestInstants.Now, session);
                await session.CommitAsync();
            }

            Assert.Null(swapped);
            FileRecord kept = (await Files.GetByIdAsync(file.Id))!;
            Assert.Equal((file.Sha256, false), (kept.Sha256, kept.Encrypted));
            Assert.Equal(before, await ChangeLog.GetLatestSequenceAsync());
        }

        [Fact]
        public async Task SwapContentAsync_FileInTheTrash_ChangesAndLogsNothing()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");
            await FileService.DeleteFileAsync(shelf, EntryReference.ForId(file.Id));
            long before = await ChangeLog.GetLatestSequenceAsync();

            await using (ContentsSession session = await Services.CreateContentsSessionAsync())
            {
                Assert.Null(await Files.SwapContentAsync(file.Id, file.Sha256, Sha256Of("new"), 3, encrypted: true, TestInstants.Now, session));
                await session.CommitAsync();
            }

            Assert.Equal(file.Sha256, (await Files.GetByIdAsync(file.Id))!.Sha256);
            Assert.Equal(before, await ChangeLog.GetLatestSequenceAsync());
        }

        [Fact]
        public async Task SwapContentAsync_FileWithMetadata_RemovesItsReadingsThenSwapsItThenLogsItsDocumentsAgain()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "plain");
            await Services.RecordContentDocumentAsync(file.Sha256, "{\"text\":\"plain\"}");
            await Services.InTransactionAsync((provider, session) => provider.GetRequiredService<IObjectMetadataRepository>()
                .RecordAsync(ObjectMetadata.Of(file.Sha256, MetadataReader.Version, TestInstants.Now, new ContentMetadata { Title = "plain" }), session));
            await Services.SetFileDocumentAsync(file.Id, "{\"kept\":true}");
            long before = await ChangeLog.GetLatestSequenceAsync();

            await using (ContentsSession session = await Services.CreateContentsSessionAsync())
            {
                await Files.SwapContentAsync(file.Id, file.Sha256, Sha256Of("new"), 3, encrypted: true, TestInstants.Now, session);
                await session.CommitAsync();
            }

            Assert.Null(await Services.GetContentDocumentAsync(file.Sha256));
            Assert.Null(await Services.GetRequiredService<IObjectMetadataRepository>().GetBySha256Async(file.Sha256));
            Assert.Equal(
                [
                    (ChangeEntityType.ObjectMetadata, ChangeAction.Released),
                    (ChangeEntityType.ContentPluginMetadata, ChangeAction.Released),
                    (ChangeEntityType.File, ChangeAction.Swapped),
                    (ChangeEntityType.FilePluginMetadata, ChangeAction.Updated),
                ],
                (await ChangeLog.GetAfterAsync(before, 100)).Select(entry => (entry.EntityType, entry.Action)));
        }
    }
}
