using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Repositories;
using ByteShelf.Server.Storage.Services;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;
using EasyReasy.Database;
using Microsoft.Extensions.DependencyInjection;

namespace ByteShelf.Server.Tests.Storage
{
    public class StorageMaintenanceTests : ServiceTestBase
    {
        private IStorageMaintenance Maintenance => Services.GetRequiredService<IStorageMaintenance>();

        /// <summary>A process start before anything in the test, so no upload counts as interrupted by a restart.</summary>
        private static DateTimeOffset LongAgo => TestInstants.Now - TimeSpan.FromDays(365);

        [Fact]
        public async Task RunAsync_ResumableUploadIdleTooLong_RemovesIt()
        {
            Shelf shelf = await CreateShelfAsync();
            UploadSession upload = await UploadService.StartAsync(shelf, new StartUploadRequest { Path = "/a", SizeBytes = 5 });
            Clock.Advance(Options.AbandonedUploadAge + TimeSpan.FromSeconds(1));

            StorageMaintenanceSummary summary = await Maintenance.RunAsync(LongAgo, CancellationToken.None);

            Assert.Equal(1, summary.AbandonedUploads);
            Assert.Empty(StagingArea.List());
            await Assert.ThrowsAsync<NotFoundException>(() => UploadService.GetAsync(shelf, upload.Id));
        }

        [Fact]
        public async Task RunAsync_ResumableUploadRecentlyActive_KeepsItAcrossARestart()
        {
            Shelf shelf = await CreateShelfAsync();
            UploadSession upload = await UploadService.StartAsync(shelf, new StartUploadRequest { Path = "/a", SizeBytes = 5 });
            Clock.Advance(Options.AbandonedUploadAge - TimeSpan.FromMinutes(1));
            await UploadService.AppendAsync(shelf, upload.Id, 0, new MemoryStream(new byte[2]), CancellationToken.None);
            Clock.Advance(TimeSpan.FromMinutes(2));

            StorageMaintenanceSummary summary = await Maintenance.RunAsync(Clock.GetUtcNow(), CancellationToken.None);

            Assert.Equal(0, summary.AbandonedUploads);
            Assert.Equal(2, (await UploadService.GetAsync(shelf, upload.Id)).ReceivedBytes);
        }

        [Fact]
        public async Task RunAsync_SinglePutStartedBeforeTheProcess_RemovesIt()
        {
            Shelf shelf = await CreateShelfAsync();
            await InsertSinglePutAsync(shelf, startedAt: Clock.GetUtcNow());

            StorageMaintenanceSummary summary = await Maintenance.RunAsync(Clock.GetUtcNow() + TimeSpan.FromSeconds(1), CancellationToken.None);

            Assert.Equal(1, summary.AbandonedUploads);
        }

        [Fact]
        public async Task RunAsync_SinglePutStartedAfterTheProcess_KeepsIt()
        {
            Shelf shelf = await CreateShelfAsync();
            await InsertSinglePutAsync(shelf, startedAt: Clock.GetUtcNow());

            StorageMaintenanceSummary summary = await Maintenance.RunAsync(Clock.GetUtcNow() - TimeSpan.FromSeconds(1), CancellationToken.None);

            Assert.Equal(0, summary.AbandonedUploads);
        }

        [Fact]
        public async Task RunAsync_ChunkArrivesWhileWaitingForTheUploadLock_KeepsTheUpload()
        {
            Shelf shelf = await CreateShelfAsync();
            UploadSession upload = await UploadService.StartAsync(shelf, new StartUploadRequest { Path = "/a", SizeBytes = 5 });
            Clock.Advance(Options.AbandonedUploadAge + TimeSpan.FromSeconds(1));
            UploadLocks locks = Services.GetRequiredService<UploadLocks>();

            Task<StorageMaintenanceSummary> run;
            using (await locks.AcquireAsync(upload.Id))
            {
                // Maintenance reads the upload as abandoned, then waits for its lock while a chunk lands.
                run = Maintenance.RunAsync(LongAgo, CancellationToken.None);
                await Task.Delay(100);
                await Services.GetRequiredService<IUploadRepository>().UpdateReceivedAsync(upload.Id, 0, 0, Clock.GetUtcNow());
            }

            StorageMaintenanceSummary summary = await run;

            Assert.Equal(0, summary.AbandonedUploads);
            Assert.Equal(upload.Id, (await UploadService.GetAsync(shelf, upload.Id)).Id);
        }

        [Fact]
        public async Task RunAsync_StagingEntriesWithoutUpload_RemovesThem()
        {
            await CreateShelfAsync();
            await File.WriteAllTextAsync(StagingArea.GetPath(Guid.NewGuid()), "left behind");
            await File.WriteAllTextAsync(Path.Combine(TestRoot.Root.StagingPath, "not-an-id"), "x");
            Directory.CreateDirectory(Path.Combine(TestRoot.Root.StagingPath, "a-directory"));

            StorageMaintenanceSummary summary = await Maintenance.RunAsync(LongAgo, CancellationToken.None);

            Assert.Equal(3, summary.StrayStagingEntries);
            Assert.Empty(StagingArea.List());
        }

        [Fact]
        public async Task RunAsync_ObjectNoRowPointsAt_RemovesItAndKeepsReferencedOnes()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile kept = await StoreTextAsync(shelf, "/kept.txt", "kept");
            string orphanHash = Sha256Of("orphan");
            string orphanSource = Path.Combine(TestRoot.Root.Path, "orphan.tmp");
            await File.WriteAllTextAsync(orphanSource, "orphan");
            ObjectStore.Place(orphanSource, orphanHash);

            StorageMaintenanceSummary summary = await Maintenance.RunAsync(LongAgo, CancellationToken.None);

            Assert.Equal(1, summary.RemovedObjects);
            Assert.False(ObjectStore.Exists(orphanHash));
            Assert.True(ObjectStore.Exists(kept.Sha256));
        }

        [Fact]
        public async Task RunAsync_TrashedFileStillRestorable_KeepsItsObject()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "trashed");
            await FileService.DeleteFileAsync(shelf, EntryReference.ForPath("/a.txt"));

            StorageMaintenanceSummary summary = await Maintenance.RunAsync(LongAgo, CancellationToken.None);

            Assert.Equal(0, summary.RemovedObjects);
            Assert.True(ObjectStore.Exists(file.Sha256));
        }

        /// <summary>The row a single PUT leaves when its process dies before the request ends.</summary>
        private async Task InsertSinglePutAsync(Shelf shelf, DateTimeOffset startedAt)
        {
            UploadRecord upload = new UploadRecord
            {
                Id = Guid.NewGuid(),
                ShelfId = shelf.Id,
                Path = "/interrupted",
                ContentType = ContentTypes.Default,
                SizeBytes = 100,
                ReceivedBytes = 0,
                Overwrite = false,
                IsResumable = false,
                CreatedAt = startedAt,
                LastActivityAt = startedAt,
            };

            await using IDbTransactionSession session = await TestRoot.SessionFactory.CreateSessionWithTransactionAsync();
            await Services.GetRequiredService<IUploadRepository>().InsertAsync(upload, session);
            await session.CommitAsync();
        }
    }
}
