using System.Diagnostics;
using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Derived.Services;
using ByteShelf.Server.Plugins.Processes;
using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Repositories;
using ByteShelf.Server.Storage.Services;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;
using EasyReasy.Database.Mapping;
using EasyReasy.Database;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace ByteShelf.Server.Tests.Derived
{
    /// <summary>
    /// What was derived from contents never outlives the plaintext: nothing of it is left once a file encrypted
    /// in place is answered, whatever was being made then, unless another plain file holds the contents.
    /// </summary>
    public sealed class ContentsTracesTests : DerivedTestBase
    {
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
                await MakeAsync(file, kind, PluginPriority.Asked);
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
            Task<MakingOutcome> making = MakeAsync(file);
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
            Task<MakingOutcome> makingFirst = MakeAsync(first);
            Task<MakingOutcome> makingNext = MakeAsync(next);
            using (await Plugins.StartedAsync("first"))
            {
                await EncryptInPlaceAsync(shelf, next);
                Plugins.GoOn("first");
            }

            Assert.Equal((MakingOutcome.Kept, MakingOutcome.NothingToMake), (await makingFirst, await makingNext));
            Assert.Single(Plugins.Runs);
        }

        [Fact]
        public async Task Encrypt_AsWhatWasMadeWaitsToBeKept_LeavesNothingOfItBeforeItAnswers()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", Plugins.Waiting("a"));
            Task<MakingOutcome> making = MakeAsync(file);
            using (await Plugins.StartedAsync("a"))
            {
                // Taken out of the process's folder, the run over, and its transaction not yet begun.
                _sessions.BeforeNextTransaction("MakerRun.KeepAsync", async () =>
                {
                    await Task.Run(() => EncryptInPlaceAsync(shelf, file)).WaitAsync(RealTimeLimit);
                    Assert.DoesNotContain(file.Sha256, Processes.GetContentsGiven());
                    Assert.Empty(AllDerivedFiles());
                });
                Plugins.GoOn("a");
            }

            Assert.Equal(MakingOutcome.NothingToMake, await making);
            Assert.Empty(AllDerivedFiles());
        }

        [Fact]
        public async Task Encrypt_AsWhatWasMadeIsAboutToBeKept_LeavesNothingOfIt()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", Plugins.Waiting("a"));
            Task<MakingOutcome> making = MakeAsync(file);
            Task encrypting;
            using (await Plugins.StartedAsync("a"))
            using (await Locks.LockAsync(file.Sha256))
            {
                Task keepWaits = WaitsForAsync(file.Sha256);
                Plugins.GoOn("a");
                await keepWaits;

                // Its transaction waits for the keeping one, begun before the lock was asked for.
                Task asked = _sessions.NextTransactionAskedAsync("UploadService.BeginSwapAsync");
                encrypting = Task.Run(() => EncryptInPlaceAsync(shelf, file));
                await asked.WaitAsync(RealTimeLimit);
                Assert.False(encrypting.IsCompleted);
            }

            await encrypting.WaitAsync(RealTimeLimit);

            // Kept, then removed, as it came first.
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
                    asked = MakeAsync(file, DerivedKind.Thumbnail, PluginPriority.Asked);
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
                Assert.IsType<UnauthorizedAccessException>((await Assert.ThrowsAsync<TracesNotRemovedException>(() => EncryptInPlaceAsync(shelf, file))).InnerException);
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
            Task<MakingOutcome> making = MakeAsync(file);
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
            Task<MakingOutcome> making = MakeAsync(file);
            using (await Plugins.StartedAsync("a"))
            {
                // Rows that go without releasing their contents: what is kept is checked under the lock all the same.
                await Services.InTransactionAsync((provider, session) =>
                    provider.GetRequiredService<IFileRepository>().PurgeAllWithObjectAsync(file.Sha256, TestInstants.Now, session));
                Plugins.GoOn("a");
            }

            Assert.Equal(MakingOutcome.NothingToMake, await making);
            AssertKept();
        }

        [UnixFact]
        public async Task Encrypt_OnceARunLeftWhatCannotBeRemoved_IsRefusedUntilItCan()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "undeletable");
            await GetAsync(shelf, file);
            using UndeletableFolder left = new UndeletableFolder(TestRoot, file.Sha256);

            Assert.IsType<PluginNotStoppedException>((await Assert.ThrowsAsync<TracesNotRemovedException>(() => EncryptInPlaceAsync(shelf, file))).InnerException);
            Assert.False((await FileService.GetFileAsync(shelf, EntryReference.ForId(file.Id))).Encrypted);

            left.Release();
            await EncryptInPlaceAsync(shelf, file);
            Assert.Empty(AllDerivedFiles());
        }

        [UnixFact]
        public async Task Purge_OnceARunLeftWhatCannotBeRemoved_IsRefusedAndMaintenancePurgesItOnceItCan()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "undeletable");
            await GetAsync(shelf, file);
            using UndeletableFolder left = new UndeletableFolder(TestRoot, file.Sha256);

            await FileService.DeleteFileAsync(shelf, EntryReference.ForId(file.Id));
            Assert.IsType<PluginNotStoppedException>((await Assert.ThrowsAsync<TracesNotRemovedException>(() => TrashService.EmptyAsync(shelf, CancellationToken.None))).InnerException);
            Assert.NotNull(await Services.GetRequiredService<IFileRepository>().GetByIdAsync(file.Id));

            left.Release();
            await Services.GetRequiredService<IStorageMaintenance>().RunAsync(TestInstants.Now, CancellationToken.None);
            Assert.Null(await Services.GetRequiredService<IFileRepository>().GetByIdAsync(file.Id));
            Assert.Empty(AllDerivedFiles());
            Assert.False(ObjectStore.Exists(file.Sha256));
        }

        [Fact]
        public async Task Purge_IsGoneBeforeItsTransactionCommits()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "hello");
            await GetAsync(shelf, file);
            await FileService.DeleteFileAsync(shelf, EntryReference.ForId(file.Id));
            IReadOnlyList<string>? left = null;
            _sessions.BeforeCommit = async session =>
            {
                if (await session.Connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM file WHERE id = @Id", new { file.Id }, transaction: session.Transaction) == 0)
                {
                    left = AllDerivedFiles();
                }
            };

            await TrashService.EmptyAsync(shelf, CancellationToken.None);

            Assert.Equal([], left);
        }

        [Fact]
        public async Task Commit_ReleasingSeveralContents_HoldsTheLockOfEachUntilItHasCommitted()
        {
            string[] released = [TestHashes.Sha256Of("a"), TestHashes.Sha256Of("b")];
            IReadOnlyList<bool>? free = null;
            _sessions.BeforeCommit = _ =>
            {
                free = released.Select(IsFree).ToList();
                return Task.CompletedTask;
            };

            await using (ContentsSession session = await Services.CreateContentsSessionAsync())
            {
                foreach (string sha256 in released)
                {
                    session.Release(sha256);
                }

                // As anything that knows only of a transaction commits it.
                await ((IDbSession)session).CommitAsync();
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

        private bool IsFree(string sha256)
        {
            using IDisposable? held = Locks.TryLock(sha256);
            return held != null;
        }

        private static async Task<bool> IsEncryptedAsync(ShelfFile file, IDbSession session)
        {
            return await session.Connection.QuerySingleAsync<bool>("SELECT encrypted FROM file WHERE id = @Id", new { file.Id }, transaction: session.Transaction);
        }
    }
}
