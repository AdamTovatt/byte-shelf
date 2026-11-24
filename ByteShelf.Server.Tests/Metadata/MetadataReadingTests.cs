using System.Text;
using ByteShelf.Server.ChangeLog.Models;
using ByteShelf.Server.Metadata.Models;
using ByteShelf.Server.Metadata.Repositories;
using ByteShelf.Server.Metadata.Services;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Services;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;
using EasyReasy.Database;
using EasyReasy.Database.Mapping;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ByteShelf.Server.Tests.Metadata
{
    public class MetadataReadingTests : ServiceTestBase
    {
        private FileMetadataService FileMetadataService => Services.GetRequiredService<FileMetadataService>();

        private MetadataReading Reading => Services.GetRequiredService<MetadataReading>();

        [Fact]
        public async Task RunAsync_PhotoStored_RecordsWhatItSays()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile photo = await StoreAsync(shelf, "/photo.jpg", TestMedia.Photo("2024:06:01 14:30:00", "+02:00", (59.3293, 18.0686), "Apple", "iPhone 15"), "image/jpeg");

            MetadataReadingSummary summary = await Reading.RunAsync(CancellationToken.None);

            FileMetadata metadata = await FileMetadataService.GetAsync(shelf, EntryReference.ForId(photo.Id));
            Assert.Equal(new MetadataReadingSummary(OnReplica: false, Read: 1, Failed: 0, Removed: 0), summary);
            Assert.Equal(TestInstants.Now, metadata.ReadAt);
            Assert.Equal(new DateTimeOffset(2024, 6, 1, 12, 30, 0, TimeSpan.Zero), metadata.TakenAt);
            Assert.Equal(("Apple iPhone 15", 59.3293), (metadata.Camera, Math.Round(metadata.Latitude!.Value, 5)));
        }

        [Fact]
        public async Task RunAsync_Pdf_RecordsItsTitleAndPagesAndLogsTheRow()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile pdf = await StoreAsync(shelf, "/q3.pdf", TestMedia.Pdf(pages: 4, title: "Q3 report"), "application/pdf");
            long before = await ChangeLog.GetLatestSequenceAsync();

            await Reading.RunAsync(CancellationToken.None);

            FileMetadata metadata = await FileMetadataService.GetAsync(shelf, EntryReference.ForPath("/q3.pdf"));
            Assert.Equal(("Q3 report", 4), (metadata.Title, metadata.PageCount));
            ChangeLogEntry logged = Assert.Single(await ChangeLog.GetAfterAsync(before, 100));
            Assert.Equal((ChangeEntityType.ObjectMetadata, ObjectMetadata.IdFor(pdf.Sha256), ChangeAction.Added), (logged.EntityType, logged.EntityId, logged.Action));
        }

        [Fact]
        public async Task RunAsync_SameContentsInTwoFiles_ReadsThemOnceForBoth()
        {
            Shelf shelf = await CreateShelfAsync();
            byte[] photo = TestMedia.Photo(make: "Nikon", model: "Z 6");
            await StoreAsync(shelf, "/a.jpg", photo, "image/jpeg");
            await StoreAsync(shelf, "/copies/b.jpg", photo, "image/jpeg");

            MetadataReadingSummary summary = await Reading.RunAsync(CancellationToken.None);

            Assert.Equal(1, summary.Read);
            Assert.Equal("Nikon Z 6", (await FileMetadataService.GetAsync(shelf, EntryReference.ForPath("/a.jpg"))).Camera);
            Assert.Equal("Nikon Z 6", (await FileMetadataService.GetAsync(shelf, EntryReference.ForPath("/copies/b.jpg"))).Camera);
        }

        [Fact]
        public async Task RunAsync_RunAgain_ReadsNothingTwice()
        {
            Shelf shelf = await CreateShelfAsync();
            await StoreAsync(shelf, "/photo.jpg", TestMedia.Photo(make: "Nikon"), "image/jpeg");
            await Reading.RunAsync(CancellationToken.None);

            MetadataReadingSummary again = await Reading.RunAsync(CancellationToken.None);

            Assert.Equal(0, again.Read);
        }

        [Fact]
        public async Task RunAsync_ContentsAnotherVersionRead_ReadsThemAgainOnceAndLogsIt()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile photo = await StoreAsync(shelf, "/photo.jpg", TestMedia.Photo(make: "Nikon"), "image/jpeg");
            await Services.InTransactionAsync((provider, session) => provider.GetRequiredService<IObjectMetadataRepository>()
                .RecordAsync(ObjectMetadata.Of(photo.Sha256, MetadataReader.Version - 1, TestInstants.Now, ContentMetadata.None), session));
            long before = await ChangeLog.GetLatestSequenceAsync();

            MetadataReadingSummary raised = await Reading.RunAsync(CancellationToken.None);
            MetadataReadingSummary again = await Reading.RunAsync(CancellationToken.None);

            Assert.Equal((1, 0), (raised.Read, again.Read));
            Assert.Equal("Nikon", (await FileMetadataService.GetAsync(shelf, EntryReference.ForId(photo.Id))).Camera);
            ChangeLogEntry logged = Assert.Single(await ChangeLog.GetAfterAsync(before, 100));
            Assert.Equal(ChangeAction.Updated, logged.Action);
            Assert.Equal(MetadataReader.Version, ChangePayload.Deserialize<ObjectMetadata>(logged.Payload!).ReaderVersion);
        }

        [Fact]
        public async Task GetBySha256Async_RowFromBeforeVersionsWereKept_IsVersionOnes()
        {
            string sha256 = new string('a', 64);
            await using (IDbSession session = await TestRoot.SessionFactory.CreateSessionAsync())
            {
                await session.Connection.ExecuteAsync(
                    "INSERT INTO object_metadata (id, sha256, read_at) VALUES (@Id, @Sha256, @ReadAt)",
                    new { Id = ObjectMetadata.IdFor(sha256), Sha256 = sha256, ReadAt = TestInstants.Now });
            }

            Assert.Equal(1, (await Services.GetRequiredService<IObjectMetadataRepository>().GetBySha256Async(sha256))!.ReaderVersion);
        }

        [Fact]
        public async Task RunAsync_FileOfATypeNotRead_LeavesItUnread()
        {
            Shelf shelf = await CreateShelfAsync();
            await StoreTextAsync(shelf, "/notes.txt", "%PDF-1.7 but stored as text");

            MetadataReadingSummary summary = await Reading.RunAsync(CancellationToken.None);

            Assert.Equal(0, summary.Read);
            FileMetadata metadata = await FileMetadataService.GetAsync(shelf, EntryReference.ForPath("/notes.txt"));
            Assert.Equal((false, null), (metadata.Readable, metadata.ReadAt));
        }

        [Fact]
        public async Task GetMetadataAsync_PhotoNotReadYet_IsReadableWithNothingRead()
        {
            Shelf shelf = await CreateShelfAsync();
            await StoreAsync(shelf, "/photo.jpg", TestMedia.Photo(make: "Nikon"), "image/jpeg");

            FileMetadata metadata = await FileMetadataService.GetAsync(shelf, EntryReference.ForPath("/photo.jpg"));

            Assert.Equal((true, null, null), (metadata.Readable, metadata.ReadAt, metadata.Camera));
        }

        [Fact]
        public async Task RunAsync_EncryptedFile_IsNeverRead()
        {
            Shelf shelf = await CreateShelfAsync();
            await StoreAsync(shelf, "/secret.jpg", AgeBytes, "image/jpeg", encrypted: true);

            MetadataReadingSummary summary = await Reading.RunAsync(CancellationToken.None);

            Assert.Equal(0, summary.Read);
            Assert.Null((await FileMetadataService.GetAsync(shelf, EntryReference.ForPath("/secret.jpg"))).ReadAt);
        }

        [Fact]
        public async Task GetMetadataAsync_EncryptedFileWithAPlainFilesContents_HasNone()
        {
            Shelf shelf = await CreateShelfAsync();
            await StoreAsync(shelf, "/plain.jpg", AgeBytes, "image/jpeg");
            await StoreAsync(shelf, "/secret.jpg", AgeBytes, "image/jpeg", encrypted: true);
            await Reading.RunAsync(CancellationToken.None);

            FileMetadata plain = await FileMetadataService.GetAsync(shelf, EntryReference.ForPath("/plain.jpg"));
            FileMetadata secret = await FileMetadataService.GetAsync(shelf, EntryReference.ForPath("/secret.jpg"));

            Assert.Equal((true, TestInstants.Now), (plain.Readable, plain.ReadAt));
            Assert.Equal((false, null), (secret.Readable, secret.ReadAt));
        }

        [Fact]
        public async Task RunAsync_DamagedContents_RecordsNoneOnceAndCountsThem()
        {
            Shelf shelf = await CreateShelfAsync();
            await StoreAsync(shelf, "/broken.pdf", Encoding.ASCII.GetBytes("%PDF-1.7\nnothing a PDF needs"), "application/pdf");

            MetadataReadingSummary first = await Reading.RunAsync(CancellationToken.None);
            MetadataReadingSummary second = await Reading.RunAsync(CancellationToken.None);

            FileMetadata metadata = await FileMetadataService.GetAsync(shelf, EntryReference.ForPath("/broken.pdf"));
            Assert.Equal((1, 1), (first.Read, first.Failed));
            Assert.Equal(0, second.Read);
            Assert.Equal((TestInstants.Now, null, null), (metadata.ReadAt, metadata.Title, metadata.PageCount));
        }

        [Fact]
        public async Task RunAsync_ContentsNoFileHoldsAnyMore_DropsTheirRow()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile photo = await StoreAsync(shelf, "/photo.jpg", TestMedia.Photo(make: "Nikon"), "image/jpeg");
            await Reading.RunAsync(CancellationToken.None);
            await FileService.DeleteFileAsync(shelf, EntryReference.ForId(photo.Id));
            await TrashService.EmptyAsync(shelf, CancellationToken.None);
            long before = await ChangeLog.GetLatestSequenceAsync();

            MetadataReadingSummary summary = await Reading.RunAsync(CancellationToken.None);

            Assert.Equal(1, summary.Removed);
            ChangeLogEntry logged = Assert.Single(await ChangeLog.GetAfterAsync(before, 100));
            Assert.Equal((ChangeEntityType.ObjectMetadata, ObjectMetadata.IdFor(photo.Sha256), ChangeAction.Purged, null), (logged.EntityType, logged.EntityId, logged.Action, logged.Payload));
        }

        [Fact]
        public async Task RunAsync_FileInTheTrash_KeepsItsRowForARestore()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile photo = await StoreAsync(shelf, "/photo.jpg", TestMedia.Photo(make: "Nikon"), "image/jpeg");
            await Reading.RunAsync(CancellationToken.None);
            TrashEntry deleted = await FileService.DeleteFileAsync(shelf, EntryReference.ForId(photo.Id));

            MetadataReadingSummary summary = await Reading.RunAsync(CancellationToken.None);
            await TrashService.RestoreAsync(shelf, deleted.DeletionId);

            Assert.Equal(0, summary.Removed);
            Assert.Equal("Nikon", (await FileMetadataService.GetAsync(shelf, EntryReference.ForId(photo.Id))).Camera);
        }

        [Fact]
        public async Task SwapAsync_PhotoEncryptedInPlace_DropsWhatWasReadAtOnceAndFromTheLog()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile photo = await StoreAsync(shelf, "/photo.jpg", TestMedia.Photo(position: (59.3293, 18.0686), make: "Nikon"), "image/jpeg");
            long before = await ChangeLog.GetLatestSequenceAsync();
            await Reading.RunAsync(CancellationToken.None);

            await EncryptInPlaceAsync(shelf, photo);

            Assert.Null(await Services.GetRequiredService<IObjectMetadataRepository>().GetBySha256Async(photo.Sha256));
            List<ChangeLogEntry> logged = (await ChangeLog.GetAfterAsync(before, 100)).Where(entry => entry.EntityId == ObjectMetadata.IdFor(photo.Sha256)).ToList();
            Assert.Equal([ChangeAction.Added, ChangeAction.Purged], logged.Select(entry => entry.Action));
            Assert.All(logged, entry => Assert.Null(entry.Payload));
            Assert.Equal(0, (await Reading.RunAsync(CancellationToken.None)).Removed);
        }

        [Fact]
        public async Task SwapAsync_PhotoEncryptedInPlaceWhileAPlainCopyRemains_KeepsTheCopysRow()
        {
            Shelf shelf = await CreateShelfAsync();
            byte[] contents = TestMedia.Photo(make: "Nikon");
            ShelfFile photo = await StoreAsync(shelf, "/photo.jpg", contents, "image/jpeg");
            await StoreAsync(shelf, "/copy.jpg", contents, "image/jpeg");
            await Reading.RunAsync(CancellationToken.None);

            await EncryptInPlaceAsync(shelf, photo);

            Assert.Equal("Nikon", (await FileMetadataService.GetAsync(shelf, EntryReference.ForPath("/copy.jpg"))).Camera);
            Assert.False((await FileMetadataService.GetAsync(shelf, EntryReference.ForId(photo.Id))).Readable);
        }

        [Fact]
        public async Task RunAsync_OnAReplica_ReadsNothing()
        {
            Shelf shelf = await CreateShelfAsync();
            await StoreAsync(shelf, "/photo.jpg", TestMedia.Photo(make: "Nikon"), "image/jpeg");
            await TestReplicas.MarkAsReplicaAsync(Services);

            MetadataReadingSummary summary = await Reading.RunAsync(CancellationToken.None);

            Assert.Equal(new MetadataReadingSummary(OnReplica: true, Read: 0, Failed: 0, Removed: 0), summary);
            Assert.Null((await FileMetadataService.GetAsync(shelf, EntryReference.ForPath("/photo.jpg"))).ReadAt);
        }

        [Fact]
        public async Task RunAsync_MoreThanABatch_ReadsThemAll()
        {
            Shelf shelf = await CreateShelfAsync();
            int count = MetadataReading.BatchSize + 3;
            for (int index = 0; index < count; index++)
            {
                await StoreAsync(shelf, $"/scans/{index}.pdf", TestMedia.Pdf(pages: 1, title: $"Scan {index}"), "application/pdf");
            }

            MetadataReadingSummary summary = await Reading.RunAsync(CancellationToken.None);

            Assert.Equal(count, summary.Read);
        }

        [Fact]
        public async Task Worker_ContentsStoredAfterItsFirstPass_ReadsThemWithoutWaitingForTheClock()
        {
            Shelf shelf = await CreateShelfAsync();
            PassLogger passes = new PassLogger();
            MetadataReadingWorker worker = new MetadataReadingWorker(
                Services.GetRequiredService<IServiceScopeFactory>(), Services.GetRequiredService<MetadataReadingSignal>(), Clock, passes);

            await worker.StartAsync(CancellationToken.None);
            await passes.Finished(1).WaitAsync(TimeSpan.FromSeconds(10));
            ShelfFile photo = await StoreAsync(shelf, "/photo.jpg", TestMedia.Photo(make: "Nikon"), "image/jpeg");

            // The fake clock never moves, so only the upload's wake starts a second pass.
            await passes.Finished(2).WaitAsync(TimeSpan.FromSeconds(10));
            await worker.StopAsync(CancellationToken.None);

            Assert.Equal(TestInstants.Now, (await FileMetadataService.GetAsync(shelf, EntryReference.ForId(photo.Id))).ReadAt);
        }

        [Fact]
        public async Task RunAsync_ContentsGoneWhenRead_PassesThemOverWithoutARow()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile photo = await StoreAsync(shelf, "/photo.jpg", TestMedia.Photo(make: "Nikon"), "image/jpeg");
            ObjectStore.Delete(photo.Sha256);

            MetadataReadingSummary summary = await Reading.RunAsync(CancellationToken.None);

            Assert.Equal(new MetadataReadingSummary(OnReplica: false, Read: 0, Failed: 0, Removed: 0), summary);
            Assert.Null(await Services.GetRequiredService<IObjectMetadataRepository>().GetBySha256Async(photo.Sha256));
        }

        /// <summary>Counts the worker's finished passes, which it logs, so a test can wait for one.</summary>
        private sealed class PassLogger : ILogger<MetadataReadingWorker>
        {
            private readonly TaskCompletionSource[] _finished = [NewSignal(), NewSignal()];
            private int _count;

            public Task Finished(int pass)
            {
                return _finished[pass - 1].Task;
            }

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
                return null;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (formatter(state, exception).Contains("finished a pass", StringComparison.Ordinal))
                {
                    int pass = Interlocked.Increment(ref _count);
                    if (pass <= _finished.Length)
                    {
                        _finished[pass - 1].TrySetResult();
                    }
                }
            }

            private static TaskCompletionSource NewSignal()
            {
                return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }
    }
}
