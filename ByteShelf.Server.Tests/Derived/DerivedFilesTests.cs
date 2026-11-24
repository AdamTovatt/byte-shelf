using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Derived.Repositories;
using ByteShelf.Server.Derived.Services;
using ByteShelf.Server.Plugins.Processes;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;
using EasyReasy.Database;
using Microsoft.Extensions.DependencyInjection;

namespace ByteShelf.Server.Tests.Derived
{
    /// <summary>Derived files kept and removed with the sizes written of them, which shelves' usage counts.</summary>
    public sealed class DerivedFilesTests : DerivedTestBase
    {
        private static readonly string Sha256 = TestHashes.Sha256Of("contents");
        private static readonly DerivedEntry Failed = new DerivedEntry(TestMaker.Spec.DefaultId, 1, new DerivedOutcome.Failed());

        [Fact]
        public async Task Keep_WritesTheSizeOfWhatIsKept()
        {
            await PutAsync(Sha256, Made(DerivedKind.Thumbnail, "small"));

            Assert.Equal(Sizes((Sha256, DerivedKind.Thumbnail, "small".Length)), await SizesAsync());
        }

        [Fact]
        public async Task Keep_AFailure_RemovesTheSizeOfWhatItTakesThePlaceOf()
        {
            await PutAsync(Sha256, Made(DerivedKind.Thumbnail, "small"));

            await PutAsync(Sha256, new KeptFile(DerivedKind.Thumbnail, Failed, SourcePath: null));
            Assert.Empty(await SizesAsync());

            await PutAsync(Sha256, Made(DerivedKind.Thumbnail, "larger"));
            Assert.Equal(Sizes((Sha256, DerivedKind.Thumbnail, "larger".Length)), await SizesAsync());
        }

        [Fact]
        public async Task Keep_NotCommitted_WritesNoSize()
        {
            await using (AsyncServiceScope scope = Services.CreateAsyncScope())
            await using (IDbTransactionSession session = await scope.ServiceProvider.GetRequiredService<IDbSessionFactory>().CreateSessionWithTransactionAsync())
            {
                await scope.ServiceProvider.GetRequiredService<DerivedFiles>().KeepAsync(Sha256, [Made(DerivedKind.Thumbnail, "small")], session);
            }

            Assert.Empty(await SizesAsync());
        }

        [Fact]
        public async Task Keep_OfAFileThatIsGone_ChangesNothing()
        {
            KeptFile gone = new KeptFile(DerivedKind.Preview, MadeByTestMaker(), Path.Combine(TestRoot.Root.StagingPath, "gone"));

            await Assert.ThrowsAnyAsync<IOException>(() => PutAsync(Sha256, Made(DerivedKind.Thumbnail, "small"), gone));

            Assert.Empty(AllDerivedFiles());
            Assert.Empty(await SizesAsync());
        }

        [Fact]
        public async Task Delete_RemovesTheSizesOfWhatItRemovesAlone()
        {
            string other = TestHashes.Sha256Of("other");
            await PutAsync(Sha256, Made(DerivedKind.Thumbnail, "thumbnail"), Made(DerivedKind.Preview, "preview"), Made(DerivedKind.Agent, "agent"));
            await PutAsync(other, Made(DerivedKind.Thumbnail, "other"));

            await DeleteDerivedAsync(DerivedKind.Preview, Sha256);
            Assert.Equal(
                Sizes((Sha256, DerivedKind.Thumbnail, "thumbnail".Length), (Sha256, DerivedKind.Agent, "agent".Length), (other, DerivedKind.Thumbnail, "other".Length)),
                await SizesAsync());

            await using (AsyncServiceScope scope = Services.CreateAsyncScope())
            {
                DerivedFiles files = scope.ServiceProvider.GetRequiredService<DerivedFiles>();
                await scope.ServiceProvider.GetRequiredService<ContentsTransactions>().RunAsync(Sha256, async session =>
                {
                    await files.DeleteAsync(Sha256, session);
                    return true;
                });
            }

            Assert.Equal(Sizes((other, DerivedKind.Thumbnail, "other".Length)), await SizesAsync());
        }

        [Fact]
        public async Task TrimFull_RemovesWhatIsOverTheLimitWithItsSizeButNothingInUse()
        {
            string old = TestHashes.Sha256Of("old");
            string held = TestHashes.Sha256Of("held");
            await PutSizedAsync(old, DerivedStore.MaxFullBytes);
            Clock.Advance(TimeSpan.FromMinutes(1));
            await PutSizedAsync(held, DerivedStore.MaxFullBytes);
            Clock.Advance(TimeSpan.FromMinutes(1));
            await PutSizedAsync(Sha256, 1);

            using (await Locks.LockAsync(held))
            {
                Assert.Equal(1, await TrimFullAsync());
            }

            Assert.Equal(Sizes((held, DerivedKind.Full, DerivedStore.MaxFullBytes), (Sha256, DerivedKind.Full, 1)), await SizesAsync());
            Assert.Equal(1, await TrimFullAsync());
            Assert.Equal([Sha256], Store.EnumerateHashes(DerivedKind.Full));
            Assert.Equal(Sizes((Sha256, DerivedKind.Full, 1)), await SizesAsync());
        }

        [Fact]
        public async Task Recount_WritesWhatTheFolderHolds()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");
            ShelfFile emptied = await StoreTextAsync(shelf, "/emptied.txt", "emptied");
            ShelfFile unwritten = await StoreTextAsync(shelf, "/unwritten.txt", "unwritten");
            await PutAsync(file.Sha256, Made(DerivedKind.Thumbnail, "small"), Made(DerivedKind.Preview, "edited"));
            await PutAsync(emptied.Sha256, Made(DerivedKind.Thumbnail, "gone"));
            await PutAsync(unwritten.Sha256, Made(DerivedKind.Thumbnail, "kept"), new KeptFile(DerivedKind.Agent, Failed, SourcePath: null));
            await File.WriteAllTextAsync(Store.GetPath(DerivedKind.Preview, file.Sha256), "edited further");
            Directory.Delete(Path.GetDirectoryName(Store.GetPath(DerivedKind.Thumbnail, emptied.Sha256))!, recursive: true);
            await using (IDbTransactionSession session = await TestRoot.SessionFactory.CreateSessionWithTransactionAsync())
            {
                await Services.GetRequiredService<IDerivedSizeRepository>().RemoveAsync(unwritten.Sha256, session);
                await session.CommitAsync();
            }

            Assert.Equal(3, (await RunMaintenanceAsync()).Recounted);

            Assert.Equal(
                Sizes(
                    (file.Sha256, DerivedKind.Thumbnail, "small".Length),
                    (file.Sha256, DerivedKind.Preview, "edited further".Length),
                    (unwritten.Sha256, DerivedKind.Thumbnail, "kept".Length)),
                await SizesAsync());
            Assert.Equal(0, (await RunMaintenanceAsync()).Recounted);
        }

        [Fact]
        public async Task Recount_OfOneThatIsRightOnceItHoldsTheLock_ChangesAndCountsNothing()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "a");
            await PutAsync(file.Sha256, Made(DerivedKind.Thumbnail, "small"));
            string path = Store.GetPath(DerivedKind.Thumbnail, file.Sha256);
            await File.WriteAllTextAsync(path, "edited");

            Task<DerivedMaintenanceSummary> maintenance;
            using (await Locks.LockAsync(file.Sha256))
            {
                Task waits = WaitsForAsync(file.Sha256);
                maintenance = RunMaintenanceAsync();
                await waits;
                await File.WriteAllTextAsync(path, "small");
            }

            Assert.Equal(0, (await maintenance).Recounted);
            Assert.Equal(Sizes((file.Sha256, DerivedKind.Thumbnail, "small".Length)), await SizesAsync());
        }

        private static IReadOnlyDictionary<(string Sha256, DerivedKind Kind), long> Sizes(params (string Sha256, DerivedKind Kind, long Bytes)[] sizes)
        {
            return sizes.ToDictionary(size => (size.Sha256, size.Kind), size => size.Bytes);
        }

        private KeptFile Made(DerivedKind kind, string text)
        {
            string source = Path.Combine(TestRoot.Root.StagingPath, Guid.NewGuid().ToString("N"));
            File.WriteAllText(source, text);
            return new KeptFile(kind, MadeByTestMaker(), source);
        }

        // A sparse file, so it takes no disk.
        private Task PutSizedAsync(string sha256, long bytes)
        {
            string source = Path.Combine(TestRoot.Root.StagingPath, Guid.NewGuid().ToString("N"));
            using (FileStream file = File.Create(source))
            {
                file.SetLength(bytes);
            }

            return PutAsync(sha256, new KeptFile(DerivedKind.Full, MadeByTestMaker(), source));
        }

        private async Task<int> TrimFullAsync()
        {
            await using AsyncServiceScope scope = Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<DerivedFiles>().TrimFullAsync();
        }
    }
}
