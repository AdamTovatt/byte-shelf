using System.Text;
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
    public class UploadServiceTests : ServiceTestBase
    {
        private FailingCommitSessionFactory _sessionFactory = null!;

        protected override void ConfigureServices(IServiceCollection services)
        {
            services.AddScoped<IDbSessionFactory>(provider =>
                _sessionFactory = new FailingCommitSessionFactory(TestRoot.DataSource));
        }

        [Fact]
        public async Task StoreAsync_NewPath_StoresContentsAsObjectNamedBySha256()
        {
            Shelf shelf = await CreateShelfAsync();

            ShelfFile file = await StoreTextAsync(shelf, "/invoices/march.txt", "hello shelf");

            Assert.Equal(Sha256Of("hello shelf"), file.Sha256);
            Assert.Equal("/invoices/march.txt", file.Path);
            Assert.Equal(11, file.SizeBytes);
            string objectPath = ObjectStore.GetPath(file.Sha256);
            Assert.EndsWith(Path.Combine("objects", file.Sha256[..2], file.Sha256[2..4], file.Sha256), objectPath, StringComparison.Ordinal);
            Assert.Equal("hello shelf", await File.ReadAllTextAsync(objectPath));
        }

        [Fact]
        public async Task StoreAsync_MissingParents_CreatesEachDirectory()
        {
            Shelf shelf = await CreateShelfAsync();

            await StoreTextAsync(shelf, "/a/b/c.txt", "x");

            DirectoryListing listing = await FileService.ListDirectoryAsync(shelf, EntryReference.ForPath("/a/b"));
            Assert.Equal("/a/b", listing.Directory.Path);
            Assert.Equal("c.txt", Assert.Single(listing.Files).Name);
        }

        [Fact]
        public async Task StoreAsync_PathTakenWithoutOverwrite_ThrowsConflictAndReleasesReservation()
        {
            Shelf shelf = await CreateShelfAsync();
            await StoreTextAsync(shelf, "/report.txt", "first");

            await Assert.ThrowsAsync<ConflictException>(() => StoreTextAsync(shelf, "/report.txt", "second"));

            Assert.Equal(0, (await GetUsageAsync(shelf)).ReservedBytes);
            Assert.Empty(StagingArea.List());
        }

        [Fact]
        public async Task StoreAsync_Overwrite_ReplacesFileAndTrashesTheOldOne()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile first = await StoreTextAsync(shelf, "/report.txt", "first");

            ShelfFile second = await StoreTextAsync(shelf, "/report.txt", "second", overwrite: true);

            ShelfFile current = await FileService.GetFileAsync(shelf, EntryReference.ForPath("/report.txt"));
            Assert.Equal(second.Id, current.Id);
            TrashEntry trashed = Assert.Single(await TrashService.GetTrashAsync(shelf));
            Assert.Equal(first.Id, trashed.EntryId);
            Assert.Equal("/report.txt", trashed.Path);
        }

        [Fact]
        public async Task StoreAsync_SameContentsTwice_KeepsOneObject()
        {
            Shelf shelf = await CreateShelfAsync();

            ShelfFile first = await StoreTextAsync(shelf, "/one.txt", "same");
            ShelfFile second = await StoreTextAsync(shelf, "/two.txt", "same");

            Assert.NotEqual(first.Id, second.Id);
            Assert.Equal(first.Sha256, second.Sha256);
            Assert.Single(ObjectStore.EnumerateHashes());
        }

        [Fact]
        public async Task StoreAsync_ZeroBytes_StoresEmptyFile()
        {
            Shelf shelf = await CreateShelfAsync();

            ShelfFile file = await StoreTextAsync(shelf, "/empty", string.Empty);

            Assert.Equal(0, file.SizeBytes);
            Assert.Equal(Sha256Of(string.Empty), file.Sha256);
        }

        [Fact]
        public async Task StoreAsync_NameInDecomposedForm_IsStoredComposed()
        {
            Shelf shelf = await CreateShelfAsync();

            ShelfFile file = await StoreTextAsync(shelf, "/cafe\x0301.txt", "x");

            Assert.Equal("/caf\x00e9.txt", file.Path);
            await Assert.ThrowsAsync<ConflictException>(() => StoreTextAsync(shelf, "/caf\x00e9.txt", "y"));
        }

        [Fact]
        public async Task StoreAsync_SizeOverQuota_RefusesBeforeReadingTheBody()
        {
            Shelf shelf = await CreateShelfAsync(quotaBytes: 10);

            await Assert.ThrowsAsync<QuotaExceededException>(() => UploadService.StoreAsync(
                shelf, "/big", null, 11, false, new UnreadableStream(), CancellationToken.None));
        }

        [Fact]
        public async Task StoreAsync_QuotaCountsTrashedFiles()
        {
            Shelf shelf = await CreateShelfAsync(quotaBytes: 10);
            await StoreTextAsync(shelf, "/a", "123456");
            await FileService.DeleteFileAsync(shelf, EntryReference.ForPath("/a"));

            await Assert.ThrowsAsync<QuotaExceededException>(() => StoreTextAsync(shelf, "/b", "123456"));
        }

        [Fact]
        public async Task StoreAsync_BodyLongerThanDeclared_RefusesAndLeavesNothingBehind()
        {
            Shelf shelf = await CreateShelfAsync();
            byte[] body = Encoding.UTF8.GetBytes("eleven char");

            await Assert.ThrowsAsync<UploadOverflowException>(() => UploadService.StoreAsync(
                shelf, "/file", null, 5, false, new MemoryStream(body), CancellationToken.None));

            Assert.Equal(0, (await GetUsageAsync(shelf)).TotalBytes);
            Assert.Empty(StagingArea.List());
            Assert.Empty(ObjectStore.EnumerateHashes());
        }

        [Fact]
        public async Task StoreAsync_BodyShorterThanDeclared_Refuses()
        {
            Shelf shelf = await CreateShelfAsync();

            await Assert.ThrowsAsync<ArgumentException>(() => UploadService.StoreAsync(
                shelf, "/file", null, 50, false, new MemoryStream(new byte[10]), CancellationToken.None));

            Assert.Equal(0, (await GetUsageAsync(shelf)).TotalBytes);
        }

        [Fact]
        public async Task StoreAsync_ParentIsAFile_ThrowsConflict()
        {
            Shelf shelf = await CreateShelfAsync();
            await StoreTextAsync(shelf, "/a", "file");

            await Assert.ThrowsAsync<ConflictException>(() => StoreTextAsync(shelf, "/a/b.txt", "x"));
        }

        [Fact]
        public async Task StoreAsync_PathIsADirectory_ThrowsConflictEvenWithOverwrite()
        {
            Shelf shelf = await CreateShelfAsync();
            await StoreTextAsync(shelf, "/dir/inner.txt", "x");

            await Assert.ThrowsAsync<ConflictException>(() => StoreTextAsync(shelf, "/dir", "x", overwrite: true));
        }

        [Theory]
        [InlineData("/")]
        [InlineData("/a/../b")]
        [InlineData("/a//b")]
        [InlineData("")]
        public async Task StoreAsync_PathNotAllowed_ThrowsArgumentException(string path)
        {
            Shelf shelf = await CreateShelfAsync();

            await Assert.ThrowsAnyAsync<ArgumentException>(() => StoreTextAsync(shelf, path, "x"));
        }

        [Fact]
        public async Task StoreAsync_InvalidContentType_ThrowsArgumentException()
        {
            Shelf shelf = await CreateShelfAsync();

            await Assert.ThrowsAsync<ArgumentException>(() => UploadService.StoreAsync(
                shelf, "/a", "not a media type", 1, false, new MemoryStream(new byte[1]), CancellationToken.None));
        }

        [Fact]
        public async Task StoreAsync_CommitFailsAfterObjectPlaced_DiscardsTheUpload()
        {
            Shelf shelf = await CreateShelfAsync();

            // The first transaction reserves the upload; the second records the file and fails.
            _sessionFactory.FailTransaction(2);
            await Assert.ThrowsAsync<InvalidOperationException>(() => StoreTextAsync(shelf, "/a.txt", "placed"));

            Assert.True(ObjectStore.Exists(Sha256Of("placed")));
            Assert.Equal(0, (await GetUsageAsync(shelf)).TotalBytes);
            Assert.Empty(StagingArea.List());
            await Assert.ThrowsAsync<NotFoundException>(() => FileService.GetFileAsync(shelf, EntryReference.ForPath("/a.txt")));
        }

        [Fact]
        public async Task StartAsync_ReservesDeclaredSizeAgainstQuota()
        {
            Shelf shelf = await CreateShelfAsync(quotaBytes: 100);

            await UploadService.StartAsync(shelf, new StartUploadRequest { Path = "/a", SizeBytes = 60 });

            Assert.Equal(60, (await GetUsageAsync(shelf)).ReservedBytes);
            await Assert.ThrowsAsync<QuotaExceededException>(() =>
                UploadService.StartAsync(shelf, new StartUploadRequest { Path = "/b", SizeBytes = 41 }));
        }

        [Fact]
        public async Task CompleteAsync_ChunksSentInOrder_StoresWholeFile()
        {
            Shelf shelf = await CreateShelfAsync();
            UploadSession upload = await UploadService.StartAsync(shelf, new StartUploadRequest { Path = "/big.bin", SizeBytes = 10, ContentType = "application/x-test" });

            await UploadService.AppendAsync(shelf, upload.Id, 0, new MemoryStream(Encoding.UTF8.GetBytes("01234")), CancellationToken.None);
            UploadSession progress = await UploadService.AppendAsync(shelf, upload.Id, 5, new MemoryStream(Encoding.UTF8.GetBytes("56789")), CancellationToken.None);
            ShelfFile file = await UploadService.CompleteAsync(shelf, upload.Id, CancellationToken.None);

            Assert.Equal(10, progress.ReceivedBytes);
            Assert.Equal(Sha256Of("0123456789"), file.Sha256);
            Assert.Equal("application/x-test", file.ContentType);
            Assert.Equal(0, (await GetUsageAsync(shelf)).ReservedBytes);
            Assert.Empty(StagingArea.List());
        }

        [Fact]
        public async Task CompleteAsync_PathTakenSinceStart_ThrowsConflictAndKeepsTheUpload()
        {
            Shelf shelf = await CreateShelfAsync();
            UploadSession upload = await UploadService.StartAsync(shelf, new StartUploadRequest { Path = "/a.txt", SizeBytes = 3 });
            await UploadService.AppendAsync(shelf, upload.Id, 0, new MemoryStream(Encoding.UTF8.GetBytes("abc")), CancellationToken.None);
            await StoreTextAsync(shelf, "/a.txt", "taken");

            await Assert.ThrowsAsync<ConflictException>(() => UploadService.CompleteAsync(shelf, upload.Id, CancellationToken.None));

            Assert.Equal(3, (await UploadService.GetAsync(shelf, upload.Id)).ReceivedBytes);
            Assert.Contains(StagingArea.List(), entry => entry.UploadId == upload.Id);
        }

        [Fact]
        public async Task AppendAsync_WrongOffset_ThrowsConflictAndKeepsProgress()
        {
            Shelf shelf = await CreateShelfAsync();
            UploadSession upload = await UploadService.StartAsync(shelf, new StartUploadRequest { Path = "/f", SizeBytes = 10 });
            await UploadService.AppendAsync(shelf, upload.Id, 0, new MemoryStream(new byte[4]), CancellationToken.None);

            await Assert.ThrowsAsync<ConflictException>(() =>
                UploadService.AppendAsync(shelf, upload.Id, 2, new MemoryStream(new byte[2]), CancellationToken.None));

            Assert.Equal(4, (await UploadService.GetAsync(shelf, upload.Id)).ReceivedBytes);
        }

        [Fact]
        public async Task AppendAsync_PastDeclaredSize_RefusesTheChunkAndKeepsEarlierBytes()
        {
            Shelf shelf = await CreateShelfAsync();
            UploadSession upload = await UploadService.StartAsync(shelf, new StartUploadRequest { Path = "/f", SizeBytes = 6 });
            await UploadService.AppendAsync(shelf, upload.Id, 0, new MemoryStream(Encoding.UTF8.GetBytes("abc")), CancellationToken.None);

            await Assert.ThrowsAsync<UploadOverflowException>(() =>
                UploadService.AppendAsync(shelf, upload.Id, 3, new MemoryStream(Encoding.UTF8.GetBytes("defg")), CancellationToken.None));

            await UploadService.AppendAsync(shelf, upload.Id, 3, new MemoryStream(Encoding.UTF8.GetBytes("def")), CancellationToken.None);
            ShelfFile file = await UploadService.CompleteAsync(shelf, upload.Id, CancellationToken.None);
            Assert.Equal(Sha256Of("abcdef"), file.Sha256);
        }

        [Fact]
        public async Task CompleteAsync_BytesMissing_ThrowsConflict()
        {
            Shelf shelf = await CreateShelfAsync();
            UploadSession upload = await UploadService.StartAsync(shelf, new StartUploadRequest { Path = "/f", SizeBytes = 10 });
            await UploadService.AppendAsync(shelf, upload.Id, 0, new MemoryStream(new byte[9]), CancellationToken.None);

            await Assert.ThrowsAsync<ConflictException>(() => UploadService.CompleteAsync(shelf, upload.Id, CancellationToken.None));
        }

        [Fact]
        public async Task AbortAsync_ReleasesReservationAndStagedBytes()
        {
            Shelf shelf = await CreateShelfAsync();
            UploadSession upload = await UploadService.StartAsync(shelf, new StartUploadRequest { Path = "/f", SizeBytes = 10 });
            await UploadService.AppendAsync(shelf, upload.Id, 0, new MemoryStream(new byte[5]), CancellationToken.None);

            await UploadService.AbortAsync(shelf, upload.Id);

            Assert.Equal(0, (await GetUsageAsync(shelf)).ReservedBytes);
            Assert.Empty(StagingArea.List());
            await Assert.ThrowsAsync<NotFoundException>(() => UploadService.GetAsync(shelf, upload.Id));
        }

        [Fact]
        public async Task GetAsync_UploadOfAnotherShelf_ThrowsNotFound()
        {
            Shelf mine = await CreateShelfAsync("mine");
            Shelf other = await CreateShelfAsync("other");
            UploadSession upload = await UploadService.StartAsync(other, new StartUploadRequest { Path = "/f", SizeBytes = 1 });

            await Assert.ThrowsAsync<NotFoundException>(() => UploadService.GetAsync(mine, upload.Id));
        }

        private Task<ShelfUsage> GetUsageAsync(Shelf shelf)
        {
            return Services.GetRequiredService<IShelfRepository>().GetUsageAsync(shelf.Id);
        }

        /// <summary>A body that fails the test if anything reads it.</summary>
        private sealed class UnreadableStream : MemoryStream
        {
            public override int Read(byte[] buffer, int offset, int count)
            {
                throw new InvalidOperationException("The body was read.");
            }

            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                throw new InvalidOperationException("The body was read.");
            }
        }
    }
}
