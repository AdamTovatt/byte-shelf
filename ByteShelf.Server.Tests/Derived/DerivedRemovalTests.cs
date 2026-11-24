using System.Diagnostics;
using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Derived.Services;
using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Repositories;
using ByteShelf.Server.Storage.Services;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;
using EasyReasy.Database;
using EasyReasy.Database.Mapping;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ByteShelf.Server.Tests.Derived
{
    /// <summary>
    /// What was derived from contents never outlives the plaintext: nothing of it is left once a file encrypted
    /// in place is answered, whatever was being made then, unless another plain file holds the contents.
    /// </summary>
    public sealed class DerivedRemovalTests : DerivedTestBase
    {
        private static readonly TimeSpan RealTimeLimit = TimeSpan.FromSeconds(30);

        private HookedCommitSessionFactory _sessions = null!;

        protected override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);
            _sessions = new HookedCommitSessionFactory(TestRoot.Root.Database);
            services.Replace(ServiceDescriptor.Scoped<IDbSessionFactory>(_ => _sessions));
        }

        [Theory]
        [InlineData("hello")]
        [InlineData("throw secret")]
        public async Task Encrypt_WhatWasMadeOrFailed_IsGone(string contents)
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", contents);
            foreach (DerivedKind kind in DerivedKinds.All)
            {
                await Making.MakeAsync(file.Sha256, kind, Plugins.All[0], MakingPriority.Asked);
            }

            Assert.NotEmpty(AllDerivedFiles());
            await EncryptInPlaceAsync(shelf, file);

            Assert.Empty(AllDerivedFiles());
            await Assert.ThrowsAsync<NotFoundException>(() => GetAsync(shelf, file));
        }

        [Fact]
        public async Task Encrypt_WhileItIsBeingMade_EndsTheProcessAndLeavesNothingOfIt()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", Plugins.Waiting("a"));
            Task<MakingOutcome> making = Making.MakeAsync(file.Sha256, DerivedKind.Thumbnail, Plugins.All[0], MakingPriority.Backlog);
            using Process process = await Plugins.StartedAsync("a");
            Assert.NotEmpty(AllDerivedFiles());

            await EncryptInPlaceAsync(shelf, file);

            Assert.True(process.HasExited);
            Assert.Empty(AllDerivedFiles());
            Assert.Equal(MakingOutcome.Stopped, await making);
            Assert.Empty(AllDerivedFiles());
        }

        [Fact]
        public async Task Encrypt_WhileItWaitsItsTurn_IsNeverStarted()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile first = await StoreTextAsync(shelf, "/first.txt", Plugins.Waiting("first"));
            ShelfFile next = await StoreTextAsync(shelf, "/next.txt", "next");
            Task<MakingOutcome> makingFirst = Making.MakeAsync(first.Sha256, DerivedKind.Thumbnail, Plugins.All[0], MakingPriority.Backlog);
            Task<MakingOutcome> makingNext = Making.MakeAsync(next.Sha256, DerivedKind.Thumbnail, Plugins.All[0], MakingPriority.Backlog);
            using (await Plugins.StartedAsync("first"))
            {
                await EncryptInPlaceAsync(shelf, next);
                Plugins.GoOn("first");
            }

            Assert.Equal((MakingOutcome.Kept, MakingOutcome.Stopped), (await makingFirst, await makingNext));
            Assert.Single(Plugins.Runs);
        }

        [Fact]
        public async Task Encrypt_AsWhatWasMadeIsAboutToBeKept_LeavesNothingOfIt()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", Plugins.Waiting("a"));
            Task<MakingOutcome> making = Making.MakeAsync(file.Sha256, DerivedKind.Thumbnail, Plugins.All[0], MakingPriority.Backlog);
            Task encrypting;
            using (await Plugins.StartedAsync("a"))
            using (await Store.LockAsync(file.Sha256))
            {
                Task keepWaits = WaitsForAsync(file.Sha256);
                Plugins.GoOn("a");
                await keepWaits;
                Task encryptWaits = WaitsForAsync(file.Sha256);
                encrypting = EncryptInPlaceAsync(shelf, file);
                await encryptWaits;
            }

            await encrypting;

            // Kept, then removed, as it waited longer.
            Assert.Equal(MakingOutcome.Kept, await making);
            Assert.Empty(AllDerivedFiles());
        }

        [Fact]
        public async Task Encrypt_AskedForBeforeItsTransactionCommits_IsNeverStarted()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "hello");
            Task<MakingOutcome>? asked = null;
            _sessions.BeforeCommit = async session =>
            {
                if (await IsEncryptedAsync(file, session))
                {
                    // Other connections still see the plain file.
                    Task waits = WaitsForAsync(file.Sha256);
                    asked = Making.MakeAsync(file.Sha256, DerivedKind.Thumbnail, Plugins.All[0], MakingPriority.Asked);
                    await waits;
                }
            };

            await EncryptInPlaceAsync(shelf, file);

            Assert.Equal(MakingOutcome.NothingToMake, await asked!);
            Assert.Empty(Plugins.Runs);
            Assert.Empty(AllDerivedFiles());
        }

        [Fact]
        public async Task Encrypt_IsGoneBeforeItsTransactionCommits()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "hello");
            await GetAsync(shelf, file);
            IReadOnlyList<string>? left = null;
            _sessions.BeforeCommit = async session =>
            {
                if (await IsEncryptedAsync(file, session))
                {
                    left = AllDerivedFiles();
                }
            };

            await EncryptInPlaceAsync(shelf, file);

            Assert.Equal([], left);
        }

        [Fact]
        public async Task Encrypt_ThatFailsToCommit_HasItMadeAgainAsTheFileIsStillPlain()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "hello");
            await GetAsync(shelf, file);
            _sessions.BeforeCommit = async session =>
            {
                if (await IsEncryptedAsync(file, session))
                {
                    throw HookedCommitSessionFactory.CommitFailed();
                }
            };

            await Assert.ThrowsAsync<InvalidOperationException>(() => EncryptInPlaceAsync(shelf, file));

            Assert.Empty(AllDerivedFiles());
            Assert.Equal("Thumbnail of hello by test-maker 1", await GetAsync(shelf, file));
        }

        [UnixFact]
        public async Task Encrypt_WhatCannotBeRemoved_IsRefusedAndTheFileStaysPlain()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "hello");
            await GetAsync(shelf, file);
            string folder = Path.GetDirectoryName(Store.GetPath(DerivedKind.Thumbnail, file.Sha256))!;
            if (OperatingSystem.IsWindows())
            {
                return;
            }

            File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            try
            {
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => EncryptInPlaceAsync(shelf, file));
            }
            finally
            {
                File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            Assert.False((await FileService.GetFileAsync(shelf, EntryReference.ForId(file.Id))).Encrypted);
            await EncryptInPlaceAsync(shelf, file);
            Assert.Empty(AllDerivedFiles());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Encrypt_WhileAnotherPlainFileHoldsTheContents_KeepsIt(bool inTrash)
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "hello");
            ShelfFile other = await StoreTextAsync(shelf, "/b.txt", "hello");
            await GetAsync(shelf, file);
            if (inTrash)
            {
                await FileService.DeleteFileAsync(shelf, EntryReference.ForId(other.Id));
            }

            await EncryptInPlaceAsync(shelf, file);

            AssertKept((DerivedKind.Thumbnail, other), (DerivedKind.Preview, other));
            await Assert.ThrowsAsync<NotFoundException>(() => GetAsync(shelf, file));
            Assert.Single(Plugins.Runs);
        }

        [Fact]
        public async Task Encrypt_WhileAnEncryptedFileHoldsTheSameBytes_RemovesIt()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile plain = await StoreAsync(shelf, "/plain.txt", AgeBytes, "text/plain");
            await StoreAsync(shelf, "/encrypted.txt", AgeBytes, "text/plain", encrypted: true);
            await GetAsync(shelf, plain);

            // By the very same bytes too: the object stays, and it is the file that is no longer plain.
            await EncryptInPlaceAsync(shelf, plain);

            Assert.Empty(AllDerivedFiles());
        }

        [Fact]
        public async Task Purge_OfTheLastPlainFile_RemovesIt()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile plain = await StoreAsync(shelf, "/plain.txt", AgeBytes, "text/plain");
            ShelfFile copy = await StoreAsync(shelf, "/copy.txt", AgeBytes, "text/plain");
            await StoreAsync(shelf, "/encrypted.txt", AgeBytes, "text/plain", encrypted: true);
            await GetAsync(shelf, plain);

            await FileService.DeleteFileAsync(shelf, EntryReference.ForId(plain.Id));
            await TrashService.EmptyAsync(shelf, CancellationToken.None);
            AssertKept((DerivedKind.Thumbnail, copy), (DerivedKind.Preview, copy));

            await FileService.DeleteFileAsync(shelf, EntryReference.ForId(copy.Id));
            AssertKept((DerivedKind.Thumbnail, copy), (DerivedKind.Preview, copy));
            await TrashService.EmptyAsync(shelf, CancellationToken.None);
            Assert.Empty(AllDerivedFiles());
        }

        [Fact]
        public async Task Purge_WhileItIsBeingMade_LeavesNothingOfIt()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", Plugins.Waiting("a"));
            Task<MakingOutcome> making = Making.MakeAsync(file.Sha256, DerivedKind.Thumbnail, Plugins.All[0], MakingPriority.Backlog);
            using Process process = await Plugins.StartedAsync("a");

            await FileService.DeleteFileAsync(shelf, EntryReference.ForId(file.Id));
            await TrashService.EmptyAsync(shelf, CancellationToken.None);

            Assert.True(process.HasExited);
            Assert.Equal(MakingOutcome.Stopped, await making);
            Assert.Empty(AllDerivedFiles());
        }

        [Fact]
        public async Task Make_OfContentsWhoseLastFileWentMeanwhileUnnoticed_KeepsNothing()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", Plugins.Waiting("a"));
            Task<MakingOutcome> making = Making.MakeAsync(file.Sha256, DerivedKind.Thumbnail, Plugins.All[0], MakingPriority.Backlog);
            using (await Plugins.StartedAsync("a"))
            {
                // As a shelf removed on a replica takes its files, which leaves what goes with them to maintenance.
                await Services.InTransactionAsync((provider, session) =>
                    provider.GetRequiredService<IFileRepository>().PurgeAllWithObjectAsync(file.Sha256, TestInstants.Now, session));
                Plugins.GoOn("a");
            }

            Assert.Equal(MakingOutcome.NothingToMake, await making);
            Assert.Empty(AllDerivedFiles());
        }

        [UnixFact]
        public async Task Encrypt_OnceARunLeftWhatCannotBeRemoved_IsRefusedUntilItCan()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "undeletable");
            await GetAsync(shelf, file);
            using UndeletableFolder left = new UndeletableFolder(TestRoot, file.Sha256);

            await Assert.ThrowsAsync<MakerNotStoppedException>(() => EncryptInPlaceAsync(shelf, file));
            Assert.False((await FileService.GetFileAsync(shelf, EntryReference.ForId(file.Id))).Encrypted);

            left.Release();
            await EncryptInPlaceAsync(shelf, file);
            Assert.Empty(AllDerivedFiles());
        }

        [UnixFact]
        public async Task Purge_OnceARunLeftWhatCannotBeRemoved_StandsAndMaintenanceRemovesTheRest()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "undeletable");
            await GetAsync(shelf, file);
            using UndeletableFolder left = new UndeletableFolder(TestRoot, file.Sha256);

            await FileService.DeleteFileAsync(shelf, EntryReference.ForId(file.Id));
            await TrashService.EmptyAsync(shelf, CancellationToken.None);
            Assert.NotEmpty(AllDerivedFiles());

            left.Release();
            await Services.GetRequiredService<IStorageMaintenance>().RunAsync(TestInstants.Now, CancellationToken.None);
            Assert.Empty(AllDerivedFiles());
            Assert.False(ObjectStore.Exists(file.Sha256));
        }

        [Fact]
        public async Task CommitReleasing_SeveralContents_HoldsTheLockOfEachUntilItHasCommitted()
        {
            string[] released = [TestHashes.Sha256Of("a"), TestHashes.Sha256Of("b")];
            IReadOnlyList<bool>? free = null;
            _sessions.BeforeCommit = _ =>
            {
                free = released.Select(IsFree).ToList();
                return Task.CompletedTask;
            };

            await using (IDbTransactionSession session = await _sessions.CreateSessionWithTransactionAsync())
            {
                await Services.GetRequiredService<IDerivedRemoval>().CommitReleasingAsync(released, session);
            }

            Assert.Equal([false, false], free);
            Assert.Equal([true, true], released.Select(IsFree));
        }

        [Fact]
        public async Task StorageMaintenance_RemovingAnObjectNoRowPointsAt_RemovesWhatWasDerivedFromIt()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "hello");
            await GetAsync(shelf, file);
            await Services.InTransactionAsync((provider, session) =>
                provider.GetRequiredService<IFileRepository>().PurgeAllWithObjectAsync(file.Sha256, TestInstants.Now, session));
            Assert.NotEmpty(AllDerivedFiles());

            StorageMaintenanceSummary summary = await Services.GetRequiredService<IStorageMaintenance>().RunAsync(TestInstants.Now, CancellationToken.None);

            Assert.Equal(1, summary.RemovedObjects);
            Assert.Empty(AllDerivedFiles());
        }

        /// <summary>Ends once the next caller waits for what is derived from the contents.</summary>
        private async Task WaitsForAsync(string sha256)
        {
            TaskCompletionSource waits = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Store.Locks.WaitingStarted = key =>
            {
                if (key == sha256)
                {
                    waits.TrySetResult();
                }
            };

            await waits.Task.WaitAsync(RealTimeLimit);
        }

        private bool IsFree(string sha256)
        {
            using IDisposable? held = Store.Locks.TryAcquire(sha256);
            return held != null;
        }

        private static async Task<bool> IsEncryptedAsync(ShelfFile file, IDbSession session)
        {
            return await session.Connection.QuerySingleAsync<bool>("SELECT encrypted FROM file WHERE id = @Id", new { file.Id }, transaction: session.Transaction);
        }
    }
}
