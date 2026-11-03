using ByteShelf.Server.Shared.Errors;
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
    public class TrashServiceTests : ServiceTestBase
    {
        private FailingCommitSessionFactory _sessionFactory = null!;

        protected override void ConfigureServices(IServiceCollection services)
        {
            _sessionFactory = new FailingCommitSessionFactory(TestRoot.DataSource);
            services.AddSingleton<IDbSessionFactory>(_sessionFactory);
        }

        [Fact]
        public async Task DeleteFileAsync_LiveFile_HidesItAndListsTheDeletion()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/docs/a.txt", "abc");

            TrashEntry entry = await FileService.DeleteFileAsync(shelf, EntryReference.ForPath("/docs/a.txt"));

            Assert.Equal(TrashEntryKind.File, entry.Kind);
            Assert.Equal(file.Id, entry.EntryId);
            Assert.Equal("/docs/a.txt", entry.Path);
            Assert.Equal(3, entry.SizeBytes);
            Assert.Equal(TestInstants.Now + Options.RestoreWindow, entry.PurgesAt);
            await Assert.ThrowsAsync<NotFoundException>(() => FileService.GetFileAsync(shelf, EntryReference.ForPath("/docs/a.txt")));
            await Assert.ThrowsAsync<NotFoundException>(() => FileService.GetFileAsync(shelf, EntryReference.ForId(file.Id)));
        }

        [Fact]
        public async Task RestoreAsync_FileDeleted_BringsItBackWhereItWas()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/docs/a.txt", "abc");
            TrashEntry entry = await FileService.DeleteFileAsync(shelf, EntryReference.ForId(file.Id));

            await TrashService.RestoreAsync(shelf, entry.DeletionId);

            ShelfFile restored = await FileService.GetFileAsync(shelf, EntryReference.ForPath("/docs/a.txt"));
            Assert.Equal(file.Id, restored.Id);
            Assert.Empty(await TrashService.GetTrashAsync(shelf));
        }

        [Fact]
        public async Task RestoreAsync_NameTakenSinceDeletion_ThrowsConflict()
        {
            Shelf shelf = await CreateShelfAsync();
            await StoreTextAsync(shelf, "/a.txt", "old");
            TrashEntry entry = await FileService.DeleteFileAsync(shelf, EntryReference.ForPath("/a.txt"));
            await StoreTextAsync(shelf, "/a.txt", "new");

            await Assert.ThrowsAsync<ConflictException>(() => TrashService.RestoreAsync(shelf, entry.DeletionId));
        }

        [Fact]
        public async Task RestoreAsync_DirectoryWhoseNameAFileNowHas_ThrowsConflictAndKeepsBoth()
        {
            Shelf shelf = await CreateShelfAsync();
            await StoreTextAsync(shelf, "/a/x.txt", "in the directory");
            TrashEntry entry = await DirectoryService.DeleteAsync(shelf, EntryReference.ForPath("/a"));
            ShelfFile file = await StoreTextAsync(shelf, "/a", "a file now");

            await Assert.ThrowsAsync<ConflictException>(() => TrashService.RestoreAsync(shelf, entry.DeletionId));

            Assert.Equal(file.Id, (await FileService.GetFileAsync(shelf, EntryReference.ForPath("/a"))).Id);
            Assert.Equal(entry.DeletionId, Assert.Single(await TrashService.GetTrashAsync(shelf)).DeletionId);
        }

        [Fact]
        public async Task RestoreAsync_FileWhoseNameADirectoryNowHas_ThrowsConflictAndKeepsBoth()
        {
            Shelf shelf = await CreateShelfAsync();
            await StoreTextAsync(shelf, "/a", "a file");
            TrashEntry entry = await FileService.DeleteFileAsync(shelf, EntryReference.ForPath("/a"));
            ShelfDirectory directory = await DirectoryService.CreateAsync(shelf, "/a");

            await Assert.ThrowsAsync<ConflictException>(() => TrashService.RestoreAsync(shelf, entry.DeletionId));

            Assert.Equal(directory.Id, (await DirectoryService.ListAsync(shelf, EntryReference.ForPath("/a"))).Directory.Id);
            Assert.Equal(entry.DeletionId, Assert.Single(await TrashService.GetTrashAsync(shelf)).DeletionId);
        }

        [Fact]
        public async Task RestoreAsync_DeletionOfAnotherShelf_ThrowsNotFound()
        {
            Shelf mine = await CreateShelfAsync("mine");
            Shelf other = await CreateShelfAsync("other");
            await StoreTextAsync(other, "/a.txt", "x");
            TrashEntry entry = await FileService.DeleteFileAsync(other, EntryReference.ForPath("/a.txt"));

            await Assert.ThrowsAsync<NotFoundException>(() => TrashService.RestoreAsync(mine, entry.DeletionId));
        }

        [Fact]
        public async Task RestoreAsync_PastRestoreWindow_ThrowsNotFoundAndListsNothing()
        {
            Shelf shelf = await CreateShelfAsync();
            await StoreTextAsync(shelf, "/a.txt", "late");
            TrashEntry entry = await FileService.DeleteFileAsync(shelf, EntryReference.ForPath("/a.txt"));
            Clock.Advance(Options.RestoreWindow + TimeSpan.FromSeconds(1));

            await Assert.ThrowsAsync<NotFoundException>(() => TrashService.RestoreAsync(shelf, entry.DeletionId));
            Assert.Empty(await TrashService.GetTrashAsync(shelf));
        }

        [Fact]
        public async Task ListDeletionAsync_PastRestoreWindow_ThrowsNotFound()
        {
            Shelf shelf = await CreateShelfAsync();
            await StoreTextAsync(shelf, "/a.txt", "late");
            TrashEntry entry = await FileService.DeleteFileAsync(shelf, EntryReference.ForPath("/a.txt"));
            Clock.Advance(Options.RestoreWindow + TimeSpan.FromSeconds(1));

            await Assert.ThrowsAsync<NotFoundException>(() => TrashService.ListDeletionAsync(shelf, entry.DeletionId, new TreeQuery()));
        }

        [Fact]
        public async Task GetDeletedContentAsync_PastRestoreWindow_ThrowsNotFound()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "late");
            TrashEntry entry = await FileService.DeleteFileAsync(shelf, EntryReference.ForPath("/a.txt"));
            Clock.Advance(Options.RestoreWindow + TimeSpan.FromSeconds(1));

            await Assert.ThrowsAsync<NotFoundException>(() => TrashService.GetDeletedContentAsync(shelf, entry.DeletionId, file.Id));
        }

        [Fact]
        public async Task GetDeletedContentAsync_ContentsPurgedSinceTheLookup_ThrowsNotFound()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "purged under it");
            TrashEntry entry = await FileService.DeleteFileAsync(shelf, EntryReference.ForPath("/a.txt"));
            ObjectStore.Delete(file.Sha256);

            await Assert.ThrowsAsync<NotFoundException>(() => TrashService.GetDeletedContentAsync(shelf, entry.DeletionId, file.Id));
        }

        [Fact]
        public async Task GetDeletedContentAsync_ADeletionOfAnotherShelf_ThrowsNotFound()
        {
            Shelf theirs = await CreateShelfAsync("theirs");
            Shelf mine = await CreateShelfAsync("mine");
            ShelfFile file = await StoreTextAsync(theirs, "/a.txt", "theirs");
            TrashEntry entry = await FileService.DeleteFileAsync(theirs, EntryReference.ForPath("/a.txt"));

            await Assert.ThrowsAsync<NotFoundException>(() => TrashService.GetDeletedContentAsync(mine, entry.DeletionId, file.Id));
        }

        [Fact]
        public async Task RestoreAsync_FileWhoseDirectoryIsInTheTrash_ThrowsConflict()
        {
            Shelf shelf = await CreateShelfAsync();
            await StoreTextAsync(shelf, "/photos/a.jpg", "a");
            TrashEntry fileDeletion = await FileService.DeleteFileAsync(shelf, EntryReference.ForPath("/photos/a.jpg"));
            await DirectoryService.DeleteAsync(shelf, EntryReference.ForPath("/photos"));

            await Assert.ThrowsAsync<ConflictException>(() => TrashService.RestoreAsync(shelf, fileDeletion.DeletionId));
        }

        [Fact]
        public async Task PurgeExpiredAsync_PastRestoreWindow_RemovesRowAndObject()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "gone soon");
            await FileService.DeleteFileAsync(shelf, EntryReference.ForPath("/a.txt"));
            Clock.Advance(Options.RestoreWindow + TimeSpan.FromSeconds(1));

            int purged = await TrashService.PurgeExpiredAsync(CancellationToken.None);

            Assert.Equal(1, purged);
            Assert.Null(await Services.GetRequiredService<IFileRepository>().GetByIdAsync(file.Id));
            Assert.False(ObjectStore.Exists(file.Sha256));
        }

        [Fact]
        public async Task PurgeExpiredAsync_WithinRestoreWindow_KeepsEverything()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "kept");
            await FileService.DeleteFileAsync(shelf, EntryReference.ForPath("/a.txt"));
            Clock.Advance(Options.RestoreWindow - TimeSpan.FromSeconds(1));

            int purged = await TrashService.PurgeExpiredAsync(CancellationToken.None);

            Assert.Equal(0, purged);
            Assert.Single(await TrashService.GetTrashAsync(shelf));
            Assert.True(ObjectStore.Exists(file.Sha256));
        }

        [Fact]
        public async Task PurgeExpiredAsync_ObjectStillUsedByAnotherFile_KeepsTheObject()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile kept = await StoreTextAsync(shelf, "/kept.txt", "shared");
            await StoreTextAsync(shelf, "/gone.txt", "shared");
            await FileService.DeleteFileAsync(shelf, EntryReference.ForPath("/gone.txt"));
            Clock.Advance(Options.RestoreWindow + TimeSpan.FromSeconds(1));

            await TrashService.PurgeExpiredAsync(CancellationToken.None);

            Assert.Equal("shared", await ReadObjectTextAsync(kept.Sha256));
        }

        [Fact]
        public async Task PurgeExpiredAsync_DirectoryDeletion_RemovesFilesThenTheDirectory()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/photos/2026/a.jpg", "a");
            TrashEntry deletion = await DirectoryService.DeleteAsync(shelf, EntryReference.ForPath("/photos"));
            Clock.Advance(Options.RestoreWindow + TimeSpan.FromSeconds(1));

            int purged = await TrashService.PurgeExpiredAsync(CancellationToken.None);

            Assert.Equal(1, purged);
            Assert.False(ObjectStore.Exists(file.Sha256));
            IDirectoryRepository directories = Services.GetRequiredService<IDirectoryRepository>();
            Assert.Null(await directories.GetByIdAsync(deletion.EntryId));
            Assert.True(await Services.GetRequiredService<IShelfRepository>().IsEmptyAsync(shelf.Id));
        }

        [Fact]
        public async Task PurgeExpiredAsync_MoreThanOneBatchOfFilesAndDirectories_PurgesExactlyThem()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile live = await StoreTextAsync(shelf, "/kept/a.txt", "kept");
            int count = ByteShelf.Server.Storage.Services.TrashService.PurgeBatchSize + 1;
            await InsertDeletedTreeAsync(shelf, "many", count);
            Clock.Advance(Options.RestoreWindow + TimeSpan.FromSeconds(1));

            int purged = await TrashService.PurgeExpiredAsync(CancellationToken.None);

            Assert.Equal(count, purged);
            Assert.Equal(new[] { live.Id }, await IdsOnShelfAsync("file", shelf));
            Assert.Equal(await LiveDirectoryIdsAsync(shelf, "/", "/kept"), await IdsOnShelfAsync("directory", shelf));
            Assert.True(ObjectStore.Exists(live.Sha256));
        }

        [Fact]
        public async Task EmptyAsync_MoreThanOneBatchOfFilesAndDirectories_PurgesExactlyThatShelfsTrash()
        {
            Shelf mine = await CreateShelfAsync("mine");
            Shelf other = await CreateShelfAsync("other");
            ShelfFile live = await StoreTextAsync(mine, "/kept/a.txt", "kept");
            ShelfFile trashed = await StoreTextAsync(mine, "/gone.txt", "gone");
            await FileService.DeleteFileAsync(mine, EntryReference.ForPath("/gone.txt"));
            await StoreTextAsync(other, "/theirs.txt", "theirs");
            await FileService.DeleteFileAsync(other, EntryReference.ForPath("/theirs.txt"));
            int count = ByteShelf.Server.Storage.Services.TrashService.PurgeBatchSize + 1;
            await InsertDeletedTreeAsync(mine, "many", count);

            int purged = await TrashService.EmptyAsync(mine, CancellationToken.None);

            Assert.Equal(count + 1, purged);
            Assert.Equal(new[] { live.Id }, await IdsOnShelfAsync("file", mine));
            Assert.Equal(await LiveDirectoryIdsAsync(mine, "/", "/kept"), await IdsOnShelfAsync("directory", mine));
            Assert.False(ObjectStore.Exists(trashed.Sha256));
            Assert.True(ObjectStore.Exists(live.Sha256));
            Assert.Equal("/theirs.txt", Assert.Single(await TrashService.GetTrashAsync(other)).Path);
        }

        [Fact]
        public async Task EmptyAsync_StoppedPartWay_LeavesNothingRestorableAndMaintenanceFinishesIt()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/photos/a.jpg", "a");
            TrashEntry deletion = await DirectoryService.DeleteAsync(shelf, EntryReference.ForPath("/photos"));

            // The first transaction puts the trash past its window; the first purge, the second, fails.
            _sessionFactory.FailTransaction(2);
            await Assert.ThrowsAsync<InvalidOperationException>(() => TrashService.EmptyAsync(shelf, CancellationToken.None));

            Assert.Empty(await TrashService.GetTrashAsync(shelf));
            await Assert.ThrowsAsync<NotFoundException>(() => TrashService.RestoreAsync(shelf, deletion.DeletionId));
            Assert.Equal(new[] { file.Id }, await IdsOnShelfAsync("file", shelf));

            StorageMaintenanceSummary summary = await Services.GetRequiredService<IStorageMaintenance>().RunAsync(TestInstants.Now - TimeSpan.FromDays(365), CancellationToken.None);

            Assert.Equal(1, summary.PurgedFiles);
            Assert.Empty(await IdsOnShelfAsync("file", shelf));
            Assert.Equal(await LiveDirectoryIdsAsync(shelf, "/"), await IdsOnShelfAsync("directory", shelf));
            Assert.False(ObjectStore.Exists(file.Sha256));
        }

        [Fact]
        public async Task EmptyAsync_NestedDeletions_PurgesThisShelfsWholeTrashWithinItsWindow()
        {
            Shelf mine = await CreateShelfAsync("mine");
            Shelf other = await CreateShelfAsync("other");
            ShelfFile old = await StoreTextAsync(mine, "/photos/2025/old.jpg", "old");
            await StoreTextAsync(mine, "/photos/new.jpg", "new");
            ShelfFile kept = await StoreTextAsync(mine, "/kept.txt", "kept");
            await StoreTextAsync(other, "/theirs.txt", "theirs");
            await DirectoryService.DeleteAsync(mine, EntryReference.ForPath("/photos/2025"));
            await DirectoryService.DeleteAsync(mine, EntryReference.ForPath("/photos"));
            await FileService.DeleteFileAsync(other, EntryReference.ForPath("/theirs.txt"));

            int purged = await TrashService.EmptyAsync(mine, CancellationToken.None);

            Assert.Equal(2, purged);
            Assert.Empty(await TrashService.GetTrashAsync(mine));
            Assert.False(ObjectStore.Exists(old.Sha256));
            Assert.Equal(kept.Id, Assert.Single((await DirectoryService.ListAsync(mine, EntryReference.ForPath("/"))).Files).Id);
            Assert.Single(await TrashService.GetTrashAsync(other));
        }

        /// <summary>
        /// Inserts a directory under the root, deleted as one deletion with <paramref name="subdirectories"/>
        /// directories in it, each holding one file: the rows a directory delete leaves, faster than storing
        /// hundreds of files.
        /// </summary>
        private async Task InsertDeletedTreeAsync(Shelf shelf, string name, int subdirectories)
        {
            IDirectoryRepository directories = Services.GetRequiredService<IDirectoryRepository>();
            IFileRepository files = Services.GetRequiredService<IFileRepository>();
            Guid deletionId = Guid.NewGuid();
            DateTimeOffset now = Clock.GetUtcNow();
            string sha256 = Sha256Of("x");

            await using IDbTransactionSession session = await TestRoot.SessionFactory.CreateSessionWithTransactionAsync();
            DirectoryRecord root = await directories.GetRootAsync(shelf.Id, session);
            DirectoryRecord top = DeletedDirectory(shelf, root.Id, name, deletionId, now);
            await directories.InsertAsync(top, session);

            for (int i = 0; i < subdirectories; i++)
            {
                DirectoryRecord directory = DeletedDirectory(shelf, top.Id, $"directory-{i}", deletionId, now);
                await directories.InsertAsync(directory, session);
                await files.InsertAsync(
                    new FileRecord
                    {
                        Id = Guid.NewGuid(),
                        ShelfId = shelf.Id,
                        DirectoryId = directory.Id,
                        Name = "file",
                        ContentType = ContentTypes.Default,
                        SizeBytes = 1,
                        Sha256 = sha256,
                        CreatedAt = now,
                        UpdatedAt = now,
                        DeletedAt = now,
                        DeletionId = deletionId,
                    },
                    session);
            }

            await session.CommitAsync();
        }

        private static DirectoryRecord DeletedDirectory(Shelf shelf, Guid parentId, string name, Guid deletionId, DateTimeOffset at)
        {
            return new DirectoryRecord
            {
                Id = Guid.NewGuid(),
                ShelfId = shelf.Id,
                ParentId = parentId,
                Name = name,
                CreatedAt = at,
                UpdatedAt = at,
                DeletedAt = at,
                DeletionId = deletionId,
            };
        }

        /// <summary>The ids of every row of <paramref name="table"/> on the shelf, live or in the trash, in order.</summary>
        private async Task<IReadOnlyList<Guid>> IdsOnShelfAsync(string table, Shelf shelf)
        {
            await using IDbSession session = await TestRoot.SessionFactory.CreateSessionAsync();
            IEnumerable<Guid> ids = await session.Connection.QueryAsync<Guid>($"SELECT id FROM {table} WHERE shelf_id = @shelfId", new { shelfId = shelf.Id });
            return ids.Order().ToList();
        }

        /// <summary>The ids of the live directories at <paramref name="paths"/>, in order.</summary>
        private async Task<IReadOnlyList<Guid>> LiveDirectoryIdsAsync(Shelf shelf, params string[] paths)
        {
            List<Guid> ids = new List<Guid>();
            foreach (string path in paths)
            {
                ids.Add((await DirectoryService.ListAsync(shelf, EntryReference.ForPath(path))).Directory.Id);
            }

            return ids.Order().ToList();
        }
    }
}
