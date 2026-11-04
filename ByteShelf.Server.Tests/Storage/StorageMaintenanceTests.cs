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
            UploadSession upload = await UploadService.StartAsync(shelf, new StartUploadRequest { Path = "/a", SizeBytes = 5 }, Uploader);
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
            UploadSession upload = await UploadService.StartAsync(shelf, new StartUploadRequest { Path = "/a", SizeBytes = 5 }, Uploader);
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
            UploadRecord upload = await InsertSinglePutAsync(shelf, startedAt: Clock.GetUtcNow());

            StorageMaintenanceSummary summary = await Maintenance.RunAsync(Clock.GetUtcNow() + TimeSpan.FromSeconds(1), CancellationToken.None);

            Assert.Equal(1, summary.AbandonedUploads);
            Assert.Null(await Services.GetRequiredService<IUploadRepository>().GetByIdAsync(upload.Id));
            Assert.Empty(StagingArea.List());
            Assert.Equal(0, (await Services.GetRequiredService<IShelfRepository>().GetUsageAsync(shelf.Id)).ReservedBytes);
        }

        [Fact]
        public async Task RunAsync_SinglePutStartedAfterTheProcess_KeepsIt()
        {
            Shelf shelf = await CreateShelfAsync();
            UploadRecord upload = await InsertSinglePutAsync(shelf, startedAt: Clock.GetUtcNow());

            StorageMaintenanceSummary summary = await Maintenance.RunAsync(Clock.GetUtcNow() - TimeSpan.FromSeconds(1), CancellationToken.None);

            Assert.Equal(0, summary.AbandonedUploads);
            Assert.NotNull(await Services.GetRequiredService<IUploadRepository>().GetByIdAsync(upload.Id));
            Assert.Equal(upload.Id, Assert.Single(StagingArea.List()).UploadId);
        }

        [Fact]
        public async Task RunAsync_TrashPastItsWindow_PurgesItAndCountsTheFiles()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile gone = await StoreTextAsync(shelf, "/d/gone.txt", "gone");
            await StoreTextAsync(shelf, "/d/also.txt", "also");
            ShelfFile kept = await StoreTextAsync(shelf, "/kept.txt", "kept");
            await DirectoryService.DeleteAsync(shelf, EntryReference.ForPath("/d"));
            Clock.Advance(Options.RestoreWindow + TimeSpan.FromSeconds(1));
            await StoreTextAsync(shelf, "/recent.txt", "recent");
            await FileService.DeleteFileAsync(shelf, EntryReference.ForPath("/recent.txt"));

            StorageMaintenanceSummary summary = await Maintenance.RunAsync(LongAgo, CancellationToken.None);

            Assert.Equal(2, summary.PurgedFiles);
            Assert.False(ObjectStore.Exists(gone.Sha256));
            Assert.Equal("/recent.txt", Assert.Single(await TrashService.GetTrashAsync(shelf)).Path);
            Assert.Equal(kept.Id, (await FileService.GetFileAsync(shelf, EntryReference.ForPath("/kept.txt"))).Id);
        }

        [Fact]
        public async Task RunAsync_WhileTheGateIsHeld_WaitsForIt()
        {
            await CreateShelfAsync();
            await File.WriteAllTextAsync(StagingArea.GetPath(Guid.NewGuid()), "being fetched");
            MaintenanceGate gate = Services.GetRequiredService<MaintenanceGate>();

            Task<StorageMaintenanceSummary> run;
            using (await gate.EnterAsync(CancellationToken.None))
            {
                run = Maintenance.RunAsync(LongAgo, CancellationToken.None);

                // Nothing is swept while the gate is held, however long that is.
                Assert.False(run.IsCompleted);
                Assert.Single(StagingArea.List());
            }

            Assert.Equal(1, (await run.WaitAsync(TimeSpan.FromSeconds(30))).StrayStagingEntries);
        }

        [Fact]
        public async Task RunAsync_ChunkArrivesWhileWaitingForTheUploadLock_KeepsTheUpload()
        {
            Shelf shelf = await CreateShelfAsync();
            UploadSession upload = await UploadService.StartAsync(shelf, new StartUploadRequest { Path = "/a", SizeBytes = 5 }, Uploader);
            Clock.Advance(Options.AbandonedUploadAge + TimeSpan.FromSeconds(1));
            UploadLocks locks = Services.GetRequiredService<UploadLocks>();
            TaskCompletionSource waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            locks.OnWaitingStarted(uploadId =>
            {
                if (uploadId == upload.Id)
                {
                    waiting.TrySetResult();
                }
            });

            Task<StorageMaintenanceSummary> run;
            using (await locks.AcquireAsync(upload.Id))
            {
                // Maintenance reads the upload as abandoned, then waits for its lock while a chunk lands.
                run = Maintenance.RunAsync(LongAgo, CancellationToken.None);
                await waiting.Task.WaitAsync(TimeSpan.FromSeconds(30));
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

        /// <summary>The row and staging file a single PUT leaves when its process dies before the request ends.</summary>
        private async Task<UploadRecord> InsertSinglePutAsync(Shelf shelf, DateTimeOffset startedAt)
        {
            UploadRecord upload = new UploadRecord
            {
                Id = Guid.NewGuid(),
                ShelfId = shelf.Id,
                StartedBy = Uploader,
                Target = new StoreAtPath("/interrupted", Overwrite: false, Description: null),
                ContentType = ContentTypes.Default,
                SizeBytes = 100,
                ReceivedBytes = 0,
                Encrypted = false,
                IsResumable = false,
                CreatedAt = startedAt,
                LastActivityAt = startedAt,
            };

            await using (IDbTransactionSession session = await TestRoot.SessionFactory.CreateSessionWithTransactionAsync())
            {
                await Services.GetRequiredService<IUploadRepository>().InsertAsync(upload, session);
                await session.CommitAsync();
            }

            StagingArea.EnsureExists(upload.Id);
            return upload;
        }
    }
}
