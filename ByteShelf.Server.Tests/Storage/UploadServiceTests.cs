using System.Text;
using ByteShelf.Server.ChangeLog.Models;
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
            _sessionFactory = new FailingCommitSessionFactory(TestRoot.DataSource);
            services.AddSingleton<IDbSessionFactory>(_sessionFactory);
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

            DirectoryListing listing = await DirectoryService.ListAsync(shelf, EntryReference.ForPath("/a/b"));
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
        public async Task SwapAsync_EncryptingAFile_RemovesItsOldObjectAtOnce()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile plain = await StoreTextAsync(shelf, "/report.txt", "plain contents");

            ShelfFile swapped = await SwapAsync(shelf, plain, AgeBytes, encrypted: true);

            Assert.Equal((plain.Id, true), (swapped.Id, swapped.Encrypted));
            Assert.False(ObjectStore.Exists(plain.Sha256));
            Assert.True(ObjectStore.Exists(swapped.Sha256));
        }

        [Fact]
        public async Task SwapAsync_EncryptingAFile_LogsTheSwapOfThatFile()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile plain = await StoreTextAsync(shelf, "/report.txt", "plain contents");

            await SwapAsync(shelf, plain, AgeBytes, encrypted: true);

            ChangeLogEntry last = (await ChangeLog.GetAfterAsync(0, 100))[^1];
            Assert.Equal((plain.Id, ChangeAction.Swapped), (last.EntityId, last.Action));
        }

        [Fact]
        public async Task SwapAsync_OldContentsAnotherFileHolds_KeepsThemForIt()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile plain = await StoreTextAsync(shelf, "/one.txt", "same");
            await StoreTextAsync(shelf, "/two.txt", "same");

            await SwapAsync(shelf, plain, AgeBytes, encrypted: true);

            Assert.True(ObjectStore.Exists(plain.Sha256));
            Assert.Equal("same", await ReadTextAsync(shelf, "/two.txt"));
        }

        [Fact]
        public async Task SwapAsync_ExpectedHashInUppercase_SwapsAsInLowercase()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile plain = await StoreTextAsync(shelf, "/report.txt", "plain contents");

            ShelfFile swapped = await SwapAsync(shelf, plain, AgeBytes, encrypted: true, plain.Sha256.ToUpperInvariant());

            Assert.Equal((plain.Id, true), (swapped.Id, swapped.Encrypted));
        }

        [Fact]
        public async Task SwapAsync_ExpectedHashNotASha256_ThrowsArgument()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile plain = await StoreTextAsync(shelf, "/report.txt", "plain contents");

            await Assert.ThrowsAsync<ArgumentException>(() => SwapAsync(shelf, plain, AgeBytes, encrypted: true, "not-a-hash"));
        }

        [Fact]
        public async Task StoreThroughLinkAsync_FileEncryptedWhileTheBodyArrives_ThrowsConflictAndKeepsTheEncryptedFile()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile plain = await StoreTextAsync(shelf, "/report.txt", "plain contents");
            byte[] replacement = Encoding.UTF8.GetBytes("sent through the link");
            Stream body = new BeforeFirstRead(replacement, () => SwapAsync(shelf, plain, AgeBytes, encrypted: true));

            await Assert.ThrowsAsync<ConflictException>(() => UploadService.StoreThroughLinkAsync(
                shelf, Single("/report.txt", replacement.Length, "text/plain", overwrite: true), Uploader, body, CancellationToken.None));

            ShelfFile current = await FileService.GetFileAsync(shelf, EntryReference.ForPath("/report.txt"));
            Assert.Equal((plain.Id, true), (current.Id, current.Encrypted));
        }

        [Fact]
        public async Task StartSwapAsync_CompletedInChunks_SwapsInPlaceAndRemovesTheOldObject()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile plain = await StoreTextAsync(shelf, "/report.txt", "plain contents");

            UploadSession session = await UploadService.StartSwapAsync(
                shelf, new StartContentSwapRequest { FileId = plain.Id, Sha256 = plain.Sha256, SizeBytes = AgeBytes.Length, Encrypted = true }, Uploader);
            await UploadService.AppendAsync(shelf, session.Id, 0, new MemoryStream(AgeBytes[..10]), CancellationToken.None);
            await UploadService.AppendAsync(shelf, session.Id, 10, new MemoryStream(AgeBytes[10..]), CancellationToken.None);
            ShelfFile swapped = await UploadService.CompleteAsync(shelf, session.Id, CancellationToken.None);

            Assert.Equal((plain.Id, "/report.txt", true), (swapped.Id, swapped.Path, swapped.Encrypted));
            Assert.False(ObjectStore.Exists(plain.Sha256));
            Assert.Empty(await TrashService.GetTrashAsync(shelf));
        }

        [Fact]
        public async Task CompleteAsync_SwapWhoseFileWasSwappedMeanwhile_ThrowsConflictAndKeepsTheOtherSwap()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile plain = await StoreTextAsync(shelf, "/report.txt", "plain contents");
            UploadSession stale = await StartSwapWithAllBytesAsync(shelf, plain, AgeBytes);

            ShelfFile other = await SwapAsync(shelf, plain, Encoding.ASCII.GetBytes("age-encryption.org/v1\nanother client's"), encrypted: true);

            await Assert.ThrowsAsync<ConflictException>(() => UploadService.CompleteAsync(shelf, stale.Id, CancellationToken.None));
            Assert.Equal(other.Sha256, (await FileService.GetFileAsync(shelf, EntryReference.ForId(plain.Id))).Sha256);
        }

        [Fact]
        public async Task CompleteAsync_SwapWhoseFileWasDeletedMeanwhile_ThrowsNotFound()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile plain = await StoreTextAsync(shelf, "/report.txt", "plain contents");
            UploadSession stale = await StartSwapWithAllBytesAsync(shelf, plain, AgeBytes);

            await FileService.DeleteFileAsync(shelf, EntryReference.ForId(plain.Id));

            await Assert.ThrowsAsync<NotFoundException>(() => UploadService.CompleteAsync(shelf, stale.Id, CancellationToken.None));
            Assert.Equal(plain.Id, Assert.Single(await TrashService.GetTrashAsync(shelf)).EntryId);
            Assert.True(ObjectStore.Exists(plain.Sha256));
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
                shelf, Single("/big", 11), Uploader, new UnreadableStream(), CancellationToken.None));
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
        public async Task StoreAsync_ExactlyFillingTheQuota_StoresItAndRefusesOneByteMore()
        {
            Shelf shelf = await CreateShelfAsync(quotaBytes: 10);

            await StoreTextAsync(shelf, "/a", "0123456789");

            Assert.Equal(10, (await GetUsageAsync(shelf)).TotalBytes);
            await Assert.ThrowsAsync<QuotaExceededException>(() => StoreTextAsync(shelf, "/b", "x"));
        }

        [Fact]
        public async Task StoreAsync_Overwrite_CountsTheVersionItMovesToTheTrash()
        {
            Shelf shelf = await CreateShelfAsync(quotaBytes: 10);
            ShelfFile first = await StoreTextAsync(shelf, "/a", "12345");

            // Fits exactly: the new version and the trashed one fill the quota between them.
            ShelfFile second = await StoreTextAsync(shelf, "/a", "abcde", overwrite: true);

            await Assert.ThrowsAsync<QuotaExceededException>(() => StoreTextAsync(shelf, "/a", "x", overwrite: true));
            Assert.Equal(second.Id, (await FileService.GetFileAsync(shelf, EntryReference.ForPath("/a"))).Id);
            Assert.Equal(first.Id, Assert.Single(await TrashService.GetTrashAsync(shelf)).EntryId);
        }

        [Fact]
        public async Task StoreAsync_QuotaLoweredBelowUsage_KeepsWhatIsStoredAndRefusesMoreUntilFreed()
        {
            Shelf shelf = await CreateShelfAsync(quotaBytes: 10);
            ShelfFile kept = await StoreTextAsync(shelf, "/a", "12345678");

            Shelf lowered = await ShelfService.UpdateAsync(shelf, shelf.Name, quotaBytes: 5);

            QuotaExceededException refused = await Assert.ThrowsAsync<QuotaExceededException>(() => StoreTextAsync(lowered, "/b", "x"));
            Assert.Contains("has 0 bytes free of its 5", refused.Message, StringComparison.Ordinal);
            Assert.Equal(kept.Id, (await FileService.GetFileAsync(lowered, EntryReference.ForPath("/a"))).Id);

            await FileService.DeleteFileAsync(lowered, EntryReference.ForPath("/a"));
            await TrashService.EmptyAsync(lowered, CancellationToken.None);
            await StoreTextAsync(lowered, "/b", "12345");
        }

        [Fact]
        public async Task StoreAsync_TwoAtOnceToDifferentPaths_StoresBoth()
        {
            Shelf shelf = await CreateShelfAsync();

            ShelfFile[] stored = await Task.WhenAll(
                Task.Run(() => StoreTextAsync(shelf, "/a.txt", "first")),
                Task.Run(() => StoreTextAsync(shelf, "/b.txt", "second")));

            DirectoryListing listing = await DirectoryService.ListAsync(shelf, EntryReference.ForPath("/"));
            Assert.Equal(new[] { ("a.txt", stored[0].Id), ("b.txt", stored[1].Id) }, listing.Files.Select(file => (file.Name, file.Id)));
            Assert.Equal("first", await ReadObjectTextAsync(stored[0].Sha256));
            Assert.Equal("second", await ReadObjectTextAsync(stored[1].Sha256));
            Assert.Equal(0, (await GetUsageAsync(shelf)).ReservedBytes);
            Assert.Empty(StagingArea.List());
        }

        [Fact]
        public async Task StoreAsync_TwoAtOnceToTheSamePath_StoresOneAndRefusesTheOther()
        {
            Shelf shelf = await CreateShelfAsync();
            Task<ShelfFile>[] uploads =
            {
                Task.Run(() => StoreTextAsync(shelf, "/a.txt", "first")),
                Task.Run(() => StoreTextAsync(shelf, "/a.txt", "second")),
            };

            await Assert.ThrowsAsync<ConflictException>(() => Task.WhenAll(uploads));

            ShelfFile stored = await Assert.Single(uploads, upload => upload.IsCompletedSuccessfully);
            Assert.IsType<ConflictException>(Assert.Single(uploads, upload => upload.IsFaulted).Exception!.InnerException);
            Assert.Equal(stored.Id, (await FileService.GetFileAsync(shelf, EntryReference.ForPath("/a.txt"))).Id);
            Assert.Empty(await TrashService.GetTrashAsync(shelf));
            Assert.Equal(0, (await GetUsageAsync(shelf)).ReservedBytes);
            Assert.Empty(StagingArea.List());
        }

        [Fact]
        public async Task StoreAsync_TwoOverwritesAtOnceToTheSamePath_KeepsOneLiveAndTheOtherInTheTrash()
        {
            Shelf shelf = await CreateShelfAsync();

            ShelfFile[] stored = await Task.WhenAll(
                Task.Run(() => StoreTextAsync(shelf, "/a.txt", "first", overwrite: true)),
                Task.Run(() => StoreTextAsync(shelf, "/a.txt", "second", overwrite: true)));

            Guid live = (await FileService.GetFileAsync(shelf, EntryReference.ForPath("/a.txt"))).Id;
            Guid trashed = Assert.Single(await TrashService.GetTrashAsync(shelf)).EntryId;
            Assert.Equal(stored.Select(file => file.Id).Order(), new[] { live, trashed }.Order());
            Assert.Equal(0, (await GetUsageAsync(shelf)).ReservedBytes);
            Assert.Empty(StagingArea.List());
        }

        [Fact]
        public async Task StoreAsync_BodyLongerThanDeclared_RefusesAndLeavesNothingBehind()
        {
            Shelf shelf = await CreateShelfAsync();
            byte[] body = Encoding.UTF8.GetBytes("eleven char");

            await Assert.ThrowsAsync<UploadOverflowException>(() => UploadService.StoreAsync(
                shelf, Single("/file", 5), Uploader, new MemoryStream(body), CancellationToken.None));

            Assert.Equal(0, (await GetUsageAsync(shelf)).TotalBytes);
            Assert.Empty(StagingArea.List());
            Assert.Empty(ObjectStore.EnumerateHashes());
        }

        [Fact]
        public async Task StoreAsync_BodyShorterThanDeclared_Refuses()
        {
            Shelf shelf = await CreateShelfAsync();

            await Assert.ThrowsAsync<ArgumentException>(() => UploadService.StoreAsync(
                shelf, Single("/file", 50), Uploader, new MemoryStream(new byte[10]), CancellationToken.None));

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
                shelf, Single("/a", 1, "not a media type"), Uploader, new MemoryStream(new byte[1]), CancellationToken.None));
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

            await UploadService.StartAsync(shelf, new StartUploadRequest { Path = "/a", SizeBytes = 60 }, Uploader);

            Assert.Equal(60, (await GetUsageAsync(shelf)).ReservedBytes);
            await Assert.ThrowsAsync<QuotaExceededException>(() =>
                UploadService.StartAsync(shelf, new StartUploadRequest { Path = "/b", SizeBytes = 41 }, Uploader));
        }

        [Fact]
        public async Task CompleteAsync_ChunksSentInOrder_StoresWholeFile()
        {
            Shelf shelf = await CreateShelfAsync();
            UploadSession upload = await UploadService.StartAsync(shelf, new StartUploadRequest { Path = "/big.bin", SizeBytes = 10, ContentType = "application/x-test" }, Uploader);

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
            UploadSession upload = await UploadService.StartAsync(shelf, new StartUploadRequest { Path = "/a.txt", SizeBytes = 3 }, Uploader);
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
            UploadSession upload = await UploadService.StartAsync(shelf, new StartUploadRequest { Path = "/f", SizeBytes = 10 }, Uploader);
            await UploadService.AppendAsync(shelf, upload.Id, 0, new MemoryStream(new byte[4]), CancellationToken.None);

            await Assert.ThrowsAsync<ConflictException>(() =>
                UploadService.AppendAsync(shelf, upload.Id, 2, new MemoryStream(new byte[2]), CancellationToken.None));

            Assert.Equal(4, (await UploadService.GetAsync(shelf, upload.Id)).ReceivedBytes);
        }

        [Fact]
        public async Task AppendAsync_PastDeclaredSize_RefusesTheChunkAndKeepsEarlierBytes()
        {
            Shelf shelf = await CreateShelfAsync();
            UploadSession upload = await UploadService.StartAsync(shelf, new StartUploadRequest { Path = "/f", SizeBytes = 6 }, Uploader);
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
            UploadSession upload = await UploadService.StartAsync(shelf, new StartUploadRequest { Path = "/f", SizeBytes = 10 }, Uploader);
            await UploadService.AppendAsync(shelf, upload.Id, 0, new MemoryStream(new byte[9]), CancellationToken.None);

            await Assert.ThrowsAsync<ConflictException>(() => UploadService.CompleteAsync(shelf, upload.Id, CancellationToken.None));
        }

        [Fact]
        public async Task AbortAsync_ReleasesReservationAndStagedBytes()
        {
            Shelf shelf = await CreateShelfAsync();
            UploadSession upload = await UploadService.StartAsync(shelf, new StartUploadRequest { Path = "/f", SizeBytes = 10 }, Uploader);
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
            UploadSession upload = await UploadService.StartAsync(other, new StartUploadRequest { Path = "/f", SizeBytes = 1 }, Uploader);

            await Assert.ThrowsAsync<NotFoundException>(() => UploadService.GetAsync(mine, upload.Id));
        }

        private Task<ShelfUsage> GetUsageAsync(Shelf shelf)
        {
            return Services.GetRequiredService<IShelfRepository>().GetUsageAsync(shelf.Id);
        }

        /// <summary>Contents the server takes as encrypted: it checks only that they start as an age file.</summary>
        private static readonly byte[] AgeBytes = Encoding.ASCII.GetBytes("age-encryption.org/v1\nan encrypted file");

        /// <param name="expectedSha256">The hash the swap expects the file's contents to have; the file's own unless given.</param>
        private Task<ShelfFile> SwapAsync(Shelf shelf, ShelfFile file, byte[] content, bool encrypted, string? expectedSha256 = null)
        {
            StartContentSwapRequest request = new StartContentSwapRequest { FileId = file.Id, Sha256 = expectedSha256 ?? file.Sha256, SizeBytes = content.Length, Encrypted = encrypted };
            return UploadService.SwapAsync(shelf, request, Uploader, new MemoryStream(content), CancellationToken.None);
        }

        /// <summary>A resumable swap that encrypts <paramref name="file"/>, holding all its bytes but not completed yet.</summary>
        private async Task<UploadSession> StartSwapWithAllBytesAsync(Shelf shelf, ShelfFile file, byte[] content)
        {
            UploadSession session = await UploadService.StartSwapAsync(
                shelf, new StartContentSwapRequest { FileId = file.Id, Sha256 = file.Sha256, SizeBytes = content.Length, Encrypted = true }, Uploader);
            return await UploadService.AppendAsync(shelf, session.Id, 0, new MemoryStream(content), CancellationToken.None);
        }

        private async Task<string> ReadTextAsync(Shelf shelf, string path)
        {
            return await ReadObjectTextAsync((await FileService.GetFileAsync(shelf, EntryReference.ForPath(path))).Sha256);
        }

        /// <summary>A body that runs <c>meanwhile</c> before its first bytes are read, as a change made while they travel.</summary>
        private sealed class BeforeFirstRead : MemoryStream
        {
            private Func<Task>? _meanwhile;

            public BeforeFirstRead(byte[] bytes, Func<Task> meanwhile)
                : base(bytes)
            {
                _meanwhile = meanwhile;
            }

            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                Func<Task>? meanwhile = Interlocked.Exchange(ref _meanwhile, null);
                if (meanwhile != null)
                {
                    await meanwhile();
                }

                return await base.ReadAsync(buffer, cancellationToken);
            }
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
