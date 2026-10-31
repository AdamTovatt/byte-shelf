using ByteShelf.Server.ChangeLog.Models;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Repositories;
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
    }
}
