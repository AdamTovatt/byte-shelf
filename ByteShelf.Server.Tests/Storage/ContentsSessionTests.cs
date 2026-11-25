using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Repositories;
using ByteShelf.Server.Storage.Services;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;
using EasyReasy.Database;
using EasyReasy.Database.Mapping;
using Microsoft.Extensions.DependencyInjection;

namespace ByteShelf.Server.Tests.Storage
{
    /// <summary>What a change of the contents files hold has removed before it commits, and done after.</summary>
    public sealed class ContentsSessionTests : ServiceTestBase
    {
        private readonly RecordedTraces _traces = new RecordedTraces();
        private readonly List<string> _done = new List<string>();
        private readonly Guid _id = Guid.NewGuid();
        private DirectoryRecord _root = null!;

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            Shelf shelf = await CreateShelfAsync();
            _root = await Services.GetRequiredService<IDirectoryRepository>().GetRootAsync(shelf.Id);
        }

        private IFileRepository Files => Services.GetRequiredService<IFileRepository>();

        [Fact]
        public async Task Commit_RemovesTracesThenCommitsThenTellsListeners()
        {
            await using ContentsSession session = await CreateAsync();
            await WriteAsync(session);
            session.Release("old");
            session.Store("new");
            session.Store("new");
            _traces.Releasing = async () => _done.Add($"released, written: {await IsWrittenAsync()}");

            await session.CommitAsync(() => _done.Add("about to commit"));

            Assert.Equal(["released, written: False", "about to commit", "stored new"], _done);
            Assert.True(await IsWrittenAsync());
            Assert.Equal(["old"], session.Released);
            Assert.Equal([("old", session.Transaction)], _traces.Released);
            Assert.Equal(0, _traces.Held);
            Assert.Empty(_traces.Reconciled);
        }

        [Fact]
        public async Task Commit_TracesThatCannotBeRemoved_CommitsNothingAndTellsNobody()
        {
            await using (ContentsSession session = await CreateAsync())
            {
                await WriteAsync(session);
                session.Release("first");
                session.Release("second");
                session.Store("new");
                _traces.Releasing = () => _traces.Released.Count == 2 ? throw new TracesNotRemovedException("second", new IOException()) : Task.CompletedTask;

                await Assert.ThrowsAsync<TracesNotRemovedException>(() => session.CommitAsync(() => _done.Add("about to commit")));
            }

            Assert.Empty(_done);
            Assert.Equal(0, _traces.Held);
            Assert.False(await IsWrittenAsync());
            Assert.Equal([("first", 0), ("second", 0)], _traces.Reconciled);
        }

        [Fact]
        public async Task Commit_ThatFails_ReconcilesWhatItRemovedOnceItHasRolledBack()
        {
            await using ContentsSession session = await CreateAsync();
            await WriteAsync(session);
            session.Release("old");
            _traces.Reconciling = async () =>
            {
                // A write of its own, as reconciling is, waits for no transaction left open.
                await using IDbTransactionSession other = await TestRoot.SessionFactory.CreateSessionWithTransactionAsync();
                _done.Add($"reconciled, written: {await IsWrittenAsync()}");
            };

            await Assert.ThrowsAsync<IOException>(() => session.CommitAsync(() => throw new IOException("It was told to fail.")));

            Assert.Equal(["reconciled, written: False"], _done);
        }

        [Fact]
        public async Task Commit_ThatFailsAsItIsAboutTo_CommitsNothingAndTellsNobody()
        {
            await using (ContentsSession session = await CreateAsync())
            {
                await WriteAsync(session);
                session.Release("old");
                session.Store("new");

                await Assert.ThrowsAsync<IOException>(() => session.CommitAsync(() => throw new IOException("It was told to fail.")));
                Assert.Equal(0, _traces.Held);
            }

            Assert.Empty(_done);
            Assert.False(await IsWrittenAsync());
        }

        [Fact]
        public async Task Rollback_RemovesNothingAndTellsNobody()
        {
            await using (ContentsSession session = await CreateAsync())
            {
                session.Release("old");
                session.Store("new");
                await session.RollbackAsync();
            }

            Assert.Empty(_traces.Released);
            Assert.Empty(_done);
        }

        [Theory]
        [InlineData("a", true)]
        [InlineData("a encrypted", false)]
        [InlineData("a deleted", false)]
        public async Task Insert_OfALivePlainFileAlone_StoresItsContents(string state, bool stores)
        {
            FileRecord file = File(state);
            RecordedSession session = await RecordAsync(recorded => Files.InsertAsync(file, replacedId: null, recorded));

            Assert.Empty(session.Released);
            Assert.Equal(stores ? [file.Sha256] : [], session.Stored);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Swap_ReleasesWhatItHeldAndStoresWhatItHoldsWhenThatIsPlain(bool encryptsIt)
        {
            FileRecord file = File(encryptsIt ? "a" : "a encrypted");
            await RecordAsync(recorded => Files.InsertAsync(file, replacedId: null, recorded));

            RecordedSession session = await RecordAsync(recorded => Files.SwapContentAsync(file.Id, file.Sha256, Sha256Of("new"), 3, encryptsIt, TestInstants.Now, recorded));

            Assert.Equal([file.Sha256], session.Released);
            Assert.Equal(encryptsIt ? [] : [Sha256Of("new")], session.Stored);
        }

        [Fact]
        public async Task Swap_OfContentsTheFileNoLongerHolds_ReleasesAndStoresNothing()
        {
            FileRecord file = File("a");
            await RecordAsync(recorded => Files.InsertAsync(file, replacedId: null, recorded));

            RecordedSession session = await RecordAsync(recorded => Files.SwapContentAsync(file.Id, Sha256Of("then"), Sha256Of("new"), 3, encrypted: true, TestInstants.Now, recorded));

            Assert.Empty(session.Released);
            Assert.Empty(session.Stored);
        }

        /// <summary>A replica's row as it was (null for none) and as the primary sends it: the contents, whether they are encrypted and whether the file is in the trash.</summary>
        [Theory]
        [InlineData(null, "a", false, true)]
        [InlineData(null, "a encrypted", false, false)]
        [InlineData(null, "a deleted", false, false)]
        [InlineData("a", "a", false, false)]
        [InlineData("a", "a deleted", false, false)]
        [InlineData("a", "b", true, true)]
        [InlineData("a", "b encrypted", true, false)]
        [InlineData("a", "b encrypted deleted", true, false)]
        [InlineData("a encrypted", "b", true, true)]
        [InlineData("a", "a encrypted", true, false)]
        [InlineData("a encrypted", "a", true, true)]
        [InlineData("a deleted", "a", false, false)]
        [InlineData("a deleted", "b", true, true)]
        public async Task UpsertReplicated_ReleasesWhatTheRowHeldAndStoresWhatItHolds(string? before, string after, bool releases, bool stores)
        {
            if (before != null)
            {
                await RecordAsync(recorded => Files.InsertAsync(File(before), replacedId: null, recorded));
            }

            RecordedSession session = await RecordAsync(recorded => Files.UpsertReplicatedAsync(File(after), "updated", Guid.NewGuid(), TestInstants.Now, recorded));

            Assert.Equal(releases ? [File(before!).Sha256] : [], session.Released);
            Assert.Equal(stores ? [File(after).Sha256] : [], session.Stored);
        }

        [Theory]
        [InlineData("a deleted", true)]
        [InlineData("a encrypted deleted", true)]
        [InlineData("a", false)]
        public async Task Purge_ReleasesWhatTheFileHeld(string state, bool purges)
        {
            FileRecord file = File(state);
            await RecordAsync(recorded => Files.InsertAsync(file, replacedId: null, recorded));

            RecordedSession session = await RecordAsync(async recorded => Assert.Equal(purges, await Files.PurgeAsync(file.Id, TestInstants.Now, recorded)));

            Assert.Equal(purges ? [file.Sha256] : [], session.Released);
            Assert.Empty(session.Stored);
        }

        [Fact]
        public async Task PurgeForShelf_ReleasesWhatItsFilesHeld()
        {
            await RecordAsync(async recorded =>
            {
                await Files.InsertAsync(File("a"), replacedId: null, recorded);
                await Files.InsertAsync(File("a deleted", Guid.NewGuid(), "copy"), replacedId: null, recorded);
                await Files.InsertAsync(File("b encrypted deleted", Guid.NewGuid(), "other"), replacedId: null, recorded);
            });

            RecordedSession session = await RecordAsync(recorded => Files.PurgeForShelfAsync(_root.ShelfId, TestInstants.Now, recorded));

            Assert.Equal(new[] { Sha256Of("a"), Sha256Of("b") }.Order(), session.Released.Order());
            Assert.Empty(session.Stored);
        }

        /// <summary>
        /// The file (<paramref name="id"/>, or the one each test has) in a state written as its contents, then
        /// <c>encrypted</c> and <c>deleted</c> when it is.
        /// </summary>
        private FileRecord File(string state, Guid? id = null, string name = "file")
        {
            string[] words = state.Split(' ');
            bool deleted = words.Contains("deleted");
            Guid fileId = id ?? _id;
            return new FileRecord
            {
                Id = fileId,
                ShelfId = _root.ShelfId,
                DirectoryId = _root.Id,
                Name = name,
                ContentType = ContentTypes.Default,
                SizeBytes = 1,
                Sha256 = Sha256Of(words[0]),
                CreatedAt = TestInstants.Now,
                UpdatedAt = TestInstants.Now,
                Encrypted = words.Contains("encrypted"),
                DeletedAt = deleted ? TestInstants.Now : null,
                DeletionId = deleted ? fileId : null,
            };
        }

        /// <summary>Does something in a transaction; answers what it noted there.</summary>
        private async Task<RecordedSession> RecordAsync(Func<IContentsSession, Task> write)
        {
            await using IDbTransactionSession inner = await TestRoot.SessionFactory.CreateSessionWithTransactionAsync();
            RecordedSession session = new RecordedSession(inner);
            await write(session);
            await inner.CommitAsync();
            return session;
        }

        private async Task<ContentsSession> CreateAsync()
        {
            return new ContentsSession(await TestRoot.SessionFactory.CreateSessionWithTransactionAsync(), _traces, [new Listener(_done)]);
        }

        private static Task WriteAsync(IDbSession session)
        {
            return session.Connection.ExecuteAsync("CREATE TABLE written (id INTEGER)", transaction: session.Transaction);
        }

        private async Task<bool> IsWrittenAsync()
        {
            await using IDbSession session = await TestRoot.SessionFactory.CreateSessionAsync();
            return await TestQueries.CountAsync(session, "sqlite_master WHERE name = 'written'") == 1;
        }

        private sealed class Listener : IStoredContentsListener
        {
            private readonly List<string> _done;

            public Listener(List<string> done)
            {
                _done = done;
            }

            public void Stored(string sha256)
            {
                _done.Add($"stored {sha256}");
            }
        }

        private sealed class RecordedTraces : IContentsTraces
        {
            public List<(string Sha256, object? Transaction)> Released { get; } = new List<(string Sha256, object? Transaction)>();

            public int Held { get; private set; }

            public Func<Task> Releasing { get; set; } = () => Task.CompletedTask;

            public async Task<IDisposable> RemoveForCommitAsync(string sha256, IDbSession session)
            {
                Released.Add((sha256, session.Transaction));
                await Releasing();
                Held++;
                return new Hold(this);
            }

            /// <summary>Each reconciled, with how many locks were held as it was.</summary>
            public List<(string Sha256, int Held)> Reconciled { get; } = new List<(string Sha256, int Held)>();

            public Func<Task> Reconciling { get; set; } = () => Task.CompletedTask;

            public async Task ReconcileAsync(string sha256)
            {
                Reconciled.Add((sha256, Held));
                await Reconciling();
            }

            public Task<bool> RemoveUnheldAsync(string sha256, CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            private sealed class Hold : IDisposable
            {
                private readonly RecordedTraces _traces;

                public Hold(RecordedTraces traces)
                {
                    _traces = traces;
                }

                public void Dispose()
                {
                    _traces.Held--;
                }
            }
        }

        /// <summary>A transaction that keeps what is noted in it.</summary>
        private sealed class RecordedSession : IContentsSession
        {
            private readonly IDbTransactionSession _inner;

            public RecordedSession(IDbTransactionSession inner)
            {
                _inner = inner;
            }

            public List<string> Released { get; } = new List<string>();

            public List<string> Stored { get; } = new List<string>();

            public System.Data.Common.DbConnection Connection => _inner.Connection;

            public System.Data.Common.DbTransaction Transaction => _inner.Transaction;

            System.Data.Common.DbTransaction? IDbSession.Transaction => _inner.Transaction;

            public void Release(string sha256)
            {
                Released.Add(sha256);
            }

            public void Store(string sha256)
            {
                Stored.Add(sha256);
            }

            public Task CommitAsync(CancellationToken cancellationToken = default)
            {
                return _inner.CommitAsync(cancellationToken);
            }

            public Task RollbackAsync(CancellationToken cancellationToken = default)
            {
                return _inner.RollbackAsync(cancellationToken);
            }

            public ValueTask DisposeAsync()
            {
                return _inner.DisposeAsync();
            }
        }
    }
}
