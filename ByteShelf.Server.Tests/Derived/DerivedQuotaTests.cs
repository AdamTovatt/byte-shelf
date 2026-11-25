using System.Text;
using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Derived.Services;
using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Repositories;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;
using EasyReasy.Database;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ByteShelf.Server.Tests.Derived
{
    /// <summary>What plugins make of files, counted against the quotas of the shelves that hold them.</summary>
    public sealed class DerivedQuotaTests : DerivedTestBase
    {
        /// <summary>The bytes of a file that says <c>a</c>, and of what is made of it.</summary>
        private static readonly long ABytes = "a".Length;
        private static readonly long ThumbnailBytes = BytesOf(DerivedKind.Thumbnail, "a");
        private static readonly long PreviewBytes = BytesOf(DerivedKind.Preview, "a");

        private HookedCommitSessionFactory _sessions = null!;

        protected override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);
            _sessions = new HookedCommitSessionFactory(TestRoot.Root.Database);
            services.Replace(ServiceDescriptor.Scoped<IDbSessionFactory>(_ => _sessions));
        }

        [Fact]
        public async Task Usage_CountsWhatWasMadeOfTheContentsOfEachFileRow()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");
            await StoreTextAsync(shelf, "/copy.txt", "a");
            ShelfFile trashed = await StoreTextAsync(shelf, "/trashed.txt", "a");
            await FileService.DeleteFileAsync(shelf, EntryReference.ForId(trashed.Id));
            await StoreTextAsync(shelf, "/other.txt", "other");
            Shelf other = await CreateShelfAsync("other");
            await StoreTextAsync(other, "/a.txt", "a");

            await MakeAsync(file);

            long rowsOfA = 3;
            Assert.Equal((rowsOfA * (ABytes + ThumbnailBytes + PreviewBytes)) + "other".Length, await UsedBytesAsync(shelf));
            Assert.Equal(ABytes + ThumbnailBytes + PreviewBytes, await UsedBytesAsync(other));
        }

        [Fact]
        public async Task Make_ForContentsAShelfHasNoRoomFor_KeepsNothingItHasNoRoomFor()
        {
            Shelf roomy = await CreateShelfAsync("roomy");
            ShelfFile file = await StoreTextAsync(roomy, "/a.txt", "a");
            Shelf full = await CreateShelfAsync("full", quotaBytes: ABytes + ThumbnailBytes);
            await StoreTextAsync(full, "/a.txt", "a");

            Assert.Equal(MakingOutcome.Kept, await MakeAsync(file));

            AssertKept((DerivedKind.Thumbnail, file), (DerivedKind.Preview, file));
            Assert.Equal(NoRoom(PreviewBytes), Store.Find(DerivedKind.Preview, file.Sha256));
            Assert.Equal(ABytes + ThumbnailBytes, await UsedBytesAsync(full));
            Assert.Equal(ABytes + ThumbnailBytes, await UsedBytesAsync(roomy));
        }

        [Fact]
        public async Task Make_WhileAnUploadHoldsTheRoom_KeepsNothingItHasNoRoomFor()
        {
            Shelf shelf = await CreateShelfAsync("full", quotaBytes: ABytes + ThumbnailBytes + PreviewBytes);
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");
            await UploadService.StartAsync(shelf, Single("/b.bin", PreviewBytes), Uploader);

            await MakeAsync(file);

            Assert.Equal(NoRoom(PreviewBytes), Store.Find(DerivedKind.Preview, file.Sha256));
            Assert.Equal(MadeByTestMaker(), Store.Find(DerivedKind.Thumbnail, file.Sha256));
        }

        [Fact]
        public async Task Make_WhileAShelfThatDoesNotHoldTheContentsIsOverItsQuota_KeepsIt()
        {
            Shelf over = await CreateShelfAsync("over");
            await StoreTextAsync(over, "/b.txt", "bb");
            await ShelfService.UpdateAsync(over, over.Name, quotaBytes: 1);
            Shelf shelf = await CreateShelfAsync("roomy", quotaBytes: ABytes + ThumbnailBytes + PreviewBytes);
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");

            await MakeAsync(file);

            Assert.Equal(MadeByTestMaker(), Store.Find(DerivedKind.Preview, file.Sha256));
            Assert.Equal(ABytes + ThumbnailBytes + PreviewBytes, await UsedBytesAsync(shelf));
        }

        [Fact]
        public async Task Keep_OfTwoContentsAtOnceForTheLastOfTheRoom_KeepsOnlyOne()
        {
            Shelf shelf = await CreateShelfAsync("small", quotaBytes: "a".Length + "b".Length + ThumbnailBytes);
            ShelfFile a = await StoreTextAsync(shelf, "/a.txt", "a");
            ShelfFile b = await StoreTextAsync(shelf, "/b.txt", "b");
            TaskCompletionSource firstCommits = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource goOn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int commits = 0;
            _sessions.BeforeCommit = async _ =>
            {
                if (Interlocked.Increment(ref commits) == 1)
                {
                    firstCommits.SetResult();
                    await goOn.Task;
                }
            };

            Task<IReadOnlyList<KeptFile>> first = Task.Run(() => PutAsync(a.Sha256, Thumbnail("a")));
            await firstCommits.Task.WaitAsync(RealTimeLimit);
            Task asked = _sessions.NextTransactionAskedAsync("ContentsTransactions.RunAsync");
            Task<IReadOnlyList<KeptFile>> second = Task.Run(() => PutAsync(b.Sha256, Thumbnail("b")));
            await asked.WaitAsync(RealTimeLimit);
            goOn.SetResult();

            Assert.IsType<DerivedOutcome.Made>(Assert.Single(await first.WaitAsync(RealTimeLimit)).Entry.Outcome);
            Assert.Equal(new DerivedOutcome.NoRoom(ThumbnailBytes), Assert.Single(await second.WaitAsync(RealTimeLimit)).Entry.Outcome);
            Assert.Equal("a".Length + "b".Length + ThumbnailBytes, await UsedBytesAsync(shelf));
        }

        [Fact]
        public async Task Keep_ANewVersionThereIsNoRoomFor_RemovesTheOneItReplacesAndItsSize()
        {
            Shelf shelf = await CreateShelfAsync("small", quotaBytes: ABytes + ThumbnailBytes);
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");
            await PutAsync(file.Sha256, Thumbnail("a"));
            string larger = TestMaker.Made(DerivedKind.Thumbnail, "a") + "!";
            DerivedEntry version2 = new DerivedEntry(TestMaker.Spec.DefaultId, 2, new DerivedOutcome.Made(TestMaker.MediaType));

            await PutAsync(file.Sha256, KeptByTestMaker(DerivedKind.Thumbnail, Source(larger), version: 2));

            Assert.Equal(version2 with { Outcome = new DerivedOutcome.NoRoom(larger.Length) }, Store.Find(DerivedKind.Thumbnail, file.Sha256));
            Assert.False(File.Exists(Store.GetPath(DerivedKind.Thumbnail, file.Sha256)));
            Assert.Empty(await SizesAsync());
            Assert.Equal(ABytes, await UsedBytesAsync(shelf));
        }

        [Fact]
        public async Task Keep_ANewVersionNoLargerThanTheOneItReplaces_IsKeptOnAFullShelf()
        {
            Shelf shelf = await CreateShelfAsync("small", quotaBytes: ABytes + ThumbnailBytes);
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");
            await PutAsync(file.Sha256, Thumbnail("a"));
            DerivedEntry version2 = new DerivedEntry(TestMaker.Spec.DefaultId, 2, new DerivedOutcome.Made(TestMaker.MediaType));

            await PutAsync(file.Sha256, KeptByTestMaker(DerivedKind.Thumbnail, Source(TestMaker.Made(DerivedKind.Thumbnail, "b")), version: 2));

            Assert.Equal(version2, Store.Find(DerivedKind.Thumbnail, file.Sha256));
            Assert.Equal(ABytes + ThumbnailBytes, await UsedBytesAsync(shelf));
        }

        [Fact]
        public async Task MakeBacklog_OfOneThereWasNoRoomFor_MakesItOnceThereIs()
        {
            Shelf roomy = await CreateShelfAsync("roomy");
            ShelfFile file = await StoreTextAsync(roomy, "/a.txt", "a");
            Shelf full = await CreateShelfAsync("full", quotaBytes: ABytes + ThumbnailBytes);
            await StoreTextAsync(full, "/a.txt", "a");
            await MakeAsync(file);

            Assert.Equal(0, await MakeBacklogAsync());
            await ShelfService.UpdateAsync(full, full.Name, quotaBytes: ABytes + ThumbnailBytes + PreviewBytes);
            Assert.Equal(1, await MakeBacklogAsync());

            Assert.Equal(["a: Thumbnail, Preview", "a: Preview"], Plugins.Runs);
            Assert.Equal(TestMaker.Made(DerivedKind.Preview, "a"), await GetAsync(roomy, file, DerivedKind.Preview));
            Assert.Equal(ABytes + ThumbnailBytes + PreviewBytes, await UsedBytesAsync(full));
        }

        [Fact]
        public async Task MakeBacklog_OfOneThereWasNoRoomFor_WeighsItForEveryFileRowThatHoldsTheContents()
        {
            Shelf roomy = await CreateShelfAsync("roomy");
            ShelfFile file = await StoreTextAsync(roomy, "/a.txt", "a");
            Shelf full = await CreateShelfAsync("full", quotaBytes: (2 * (ABytes + ThumbnailBytes)) + PreviewBytes);
            await StoreTextAsync(full, "/a.txt", "a");
            await StoreTextAsync(full, "/copy.txt", "a");
            await MakeAsync(file);

            Assert.Equal(NoRoom(PreviewBytes), Store.Find(DerivedKind.Preview, file.Sha256));
            Assert.Equal(0, await MakeBacklogAsync());
            Assert.Single(Plugins.Runs);
        }

        [Fact]
        public async Task MakeBacklog_OfOneMissingBesideOneThereWasNoRoomFor_MakesThem()
        {
            Shelf shelf = await CreateShelfAsync("full", quotaBytes: ABytes + ThumbnailBytes);
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");
            await MakeAsync(file);
            await DeleteDerivedAsync(DerivedKind.Thumbnail, file.Sha256);

            Assert.Equal(1, await MakeBacklogAsync());

            Assert.Equal(["a: Thumbnail, Preview", "a: Thumbnail, Preview"], Plugins.Runs);
            Assert.Equal(MadeByTestMaker(), Store.Find(DerivedKind.Thumbnail, file.Sha256));
        }

        [Fact]
        public async Task Get_OfOneThereIsNoRoomFor_IsRefused()
        {
            Shelf shelf = await CreateShelfAsync("full", quotaBytes: ABytes + ThumbnailBytes);
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");
            await MakeAsync(file);

            await Assert.ThrowsAsync<QuotaExceededException>(() => GetAsync(shelf, file, DerivedKind.Preview));

            Assert.Single(Plugins.Runs);
        }

        [Fact]
        public async Task Get_OfOneThereWasNoRoomFor_MakesItOnceThereIs()
        {
            Shelf shelf = await CreateShelfAsync("full", quotaBytes: ABytes + ThumbnailBytes);
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");
            await MakeAsync(file);

            await ShelfService.UpdateAsync(shelf, shelf.Name, quotaBytes: null);

            Assert.Equal(TestMaker.Made(DerivedKind.Preview, "a"), await GetAsync(shelf, file, DerivedKind.Preview));
        }

        [Fact]
        public async Task Upload_IsRefusedAgainstWhatWasMadeToo()
        {
            Shelf shelf = await CreateShelfAsync("small", quotaBytes: ABytes + ThumbnailBytes + PreviewBytes + "c".Length);
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");
            await MakeAsync(file);

            await Assert.ThrowsAsync<QuotaExceededException>(() => StoreTextAsync(shelf, "/b.txt", "bb"));
            await StoreTextAsync(shelf, "/c.txt", "c");
        }

        [Fact]
        public async Task Upload_OfContentsSomethingWasMadeOfThatFits_IsStored()
        {
            Shelf roomy = await CreateShelfAsync("roomy");
            await MakeAsync(await StoreTextAsync(roomy, "/a.txt", "a"));
            Shelf small = await CreateShelfAsync("small", quotaBytes: ABytes + ThumbnailBytes + PreviewBytes);

            await StoreTextAsync(small, "/a.txt", "a");

            ShelfUsage usage = await GetUsageAsync(small);
            Assert.Equal((ABytes + ThumbnailBytes + PreviewBytes, 1), (usage.UsedBytes, usage.FileCount));
        }

        [Fact]
        public async Task Upload_OfContentsSomethingWasMadeOfThatDoesNotFit_IsRefusedAsItFinishesAndLeavesNothing()
        {
            Shelf roomy = await CreateShelfAsync("roomy");
            await MakeAsync(await StoreTextAsync(roomy, "/a.txt", "a"));
            Shelf small = await CreateShelfAsync("small", quotaBytes: ABytes + ThumbnailBytes + PreviewBytes - 1);

            await Assert.ThrowsAsync<QuotaExceededException>(() => StoreTextAsync(small, "/a.txt", "a"));

            ShelfUsage usage = await GetUsageAsync(small);
            Assert.Equal((0L, 0), (usage.UsedBytes, usage.FileCount));
        }

        [Fact]
        public async Task Decrypt_ToContentsSomethingWasMadeOf_CountsThatAsItFinishes()
        {
            Shelf roomy = await CreateShelfAsync("roomy");
            await MakeAsync(await StoreTextAsync(roomy, "/a.txt", "a"));
            Shelf small = await CreateShelfAsync("small");
            ShelfFile encrypted = await StoreAsync(small, "/a.txt", AgeBytes, "text/plain", encrypted: true);
            await ShelfService.UpdateAsync(small, small.Name, quotaBytes: ABytes + ThumbnailBytes + PreviewBytes - 1);
            StartContentSwapRequest request = new StartContentSwapRequest { FileId = encrypted.Id, Sha256 = encrypted.Sha256, SizeBytes = ABytes, Encrypted = false };

            // It reserves nothing, as it is smaller than what it replaces.
            await Assert.ThrowsAsync<QuotaExceededException>(() => UploadService.SwapAsync(small, request, Uploader, new MemoryStream("a"u8.ToArray()), CancellationToken.None));

            Assert.Equal(AgeBytes.Length, await UsedBytesAsync(small));
        }

        [Fact]
        public async Task Encrypt_StopsWhatWasMadeOfTheContentsCounting()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile encrypted = await StoreTextAsync(shelf, "/encrypted.txt", "encrypted");
            ShelfFile kept = await StoreTextAsync(shelf, "/kept.txt", "kept");
            await MakeAsync(encrypted);
            await MakeAsync(kept);

            await EncryptInPlaceAsync(shelf, encrypted);

            Assert.Equal([kept.Sha256], (await SizesAsync()).Keys.Select(size => size.Sha256).Distinct());
        }

        [Fact]
        public async Task Purge_StopsWhatWasMadeOfTheContentsCounting()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile purged = await StoreTextAsync(shelf, "/purged.txt", "purged");
            await MakeAsync(purged);

            await FileService.DeleteFileAsync(shelf, EntryReference.ForId(purged.Id));
            await TrashService.EmptyAsync(shelf, CancellationToken.None);

            Assert.Empty(await SizesAsync());
        }

        [Fact]
        public async Task Maintenance_AnEmptiedFolder_StopsCounting()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");
            await MakeAsync(file);

            Directory.Delete(TestRoot.Root.DerivedPath, recursive: true);

            // The sizes of the thumbnail and the preview made of it, which are gone.
            Assert.Equal(new DerivedMaintenanceSummary(Unheld: 0, Orphaned: 0, Trimmed: 0, Recounted: 2), await RunMaintenanceAsync());

            Assert.Equal(ABytes, await UsedBytesAsync(shelf));
        }

        private static long BytesOf(DerivedKind kind, string text)
        {
            return Encoding.UTF8.GetByteCount(TestMaker.Made(kind, text));
        }

        /// <summary>What <see cref="TestMaker"/> made of <c>a</c> but could not keep, as it took <paramref name="bytes"/>.</summary>
        private static DerivedEntry NoRoom(long bytes)
        {
            return MadeByTestMaker() with { Outcome = new DerivedOutcome.NoRoom(bytes) };
        }

        private KeptFile Thumbnail(string text)
        {
            return KeptByTestMaker(DerivedKind.Thumbnail, Source(TestMaker.Made(DerivedKind.Thumbnail, text)));
        }

        private string Source(string text)
        {
            string source = Path.Combine(TestRoot.Root.StagingPath, Guid.NewGuid().ToString("N"));
            File.WriteAllText(source, text);
            return source;
        }

        private async Task<ShelfUsage> GetUsageAsync(Shelf shelf)
        {
            await using AsyncServiceScope scope = Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IShelfRepository>().GetUsageAsync(shelf.Id);
        }

        private async Task<long> UsedBytesAsync(Shelf shelf)
        {
            return (await GetUsageAsync(shelf)).UsedBytes;
        }
    }
}
