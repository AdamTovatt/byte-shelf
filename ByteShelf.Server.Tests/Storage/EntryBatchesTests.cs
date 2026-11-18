using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Services;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.Storage
{
    /// <summary>What only the server's batches do; what both clients see is in <c>ClientContractTests.Batches</c>.</summary>
    public class EntryBatchesTests : ServiceTestBase
    {
        [Fact]
        public async Task MoveAsync_CallerGone_StopsBeforeTheNextItem()
        {
            Shelf shelf = await CreateShelfAsync();
            await StoreTextAsync(shelf, "/a.txt", "a");
            EntryBatches batches = new EntryBatches(FileService, DirectoryService);
            using CancellationTokenSource gone = new CancellationTokenSource();
            await gone.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => batches.MoveAsync(
                shelf,
                new MoveEntriesRequest { Moves = new[] { new EntryMove { Path = "/a.txt", ToPath = "/b.txt" } } },
                gone.Token));

            Assert.Equal("/a.txt", (await FileService.GetFileAsync(shelf, EntryReference.ForPath("/a.txt"))).Path);
        }

        [Fact]
        public async Task SetDescriptionsAsync_CallerGone_StopsBeforeTheNextItem()
        {
            Shelf shelf = await CreateShelfAsync();
            await StoreTextAsync(shelf, "/a.txt", "a");
            EntryBatches batches = new EntryBatches(FileService, DirectoryService);
            using CancellationTokenSource gone = new CancellationTokenSource();
            await gone.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => batches.SetDescriptionsAsync(
                shelf,
                new SetDescriptionsRequest { Paths = new[] { "/a.txt" }, Description = "Signed" },
                gone.Token));

            Assert.Null((await FileService.GetFileAsync(shelf, EntryReference.ForPath("/a.txt"))).Description);
        }

        [Fact]
        public async Task MoveAsync_ARequestTheRulesRefuse_ThrowsBeforeMovingAnything()
        {
            Shelf shelf = await CreateShelfAsync();
            await StoreTextAsync(shelf, "/a.txt", "a");
            EntryBatches batches = new EntryBatches(FileService, DirectoryService);

            await Assert.ThrowsAsync<ArgumentException>(() => batches.MoveAsync(
                shelf,
                new MoveEntriesRequest { Paths = new[] { "/a.txt", "/" }, IntoDirectory = "/b" },
                CancellationToken.None));

            await Assert.ThrowsAsync<NotFoundException>(() => FileService.GetFileAsync(shelf, EntryReference.ForPath("/b/a.txt")));
        }
    }
}
