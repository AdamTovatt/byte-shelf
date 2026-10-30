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
    public class TrashServiceTests : ServiceTestBase
    {
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
        public async Task GetTrashAsync_DirectoryDeletion_ListsTheDirectoryWithTotalsOfEverythingInIt()
        {
            Shelf shelf = await CreateShelfAsync();
            Guid deletionId = await InsertDeletedDirectoryAsync(shelf, "photos", fileSizes: new long[] { 10, 20 });

            TrashEntry entry = Assert.Single(await TrashService.GetTrashAsync(shelf));

            Assert.Equal(deletionId, entry.DeletionId);
            Assert.Equal(TrashEntryKind.Directory, entry.Kind);
            Assert.Equal("/photos", entry.Path);
            Assert.Equal(2, entry.FileCount);
            Assert.Equal(30, entry.SizeBytes);
        }

        [Fact]
        public async Task RestoreAsync_DirectoryDeletion_BringsBackTheDirectoryAndItsFiles()
        {
            Shelf shelf = await CreateShelfAsync();
            Guid deletionId = await InsertDeletedDirectoryAsync(shelf, "photos", fileSizes: new long[] { 10 });

            await TrashService.RestoreAsync(shelf, deletionId);

            DirectoryListing listing = await FileService.ListDirectoryAsync(shelf, EntryReference.ForPath("/photos"));
            Assert.Single(listing.Files);
        }

        [Fact]
        public async Task RestoreAsync_FileWhoseDirectoryIsInTheTrash_ThrowsConflict()
        {
            Shelf shelf = await CreateShelfAsync();
            await StoreTextAsync(shelf, "/photos/a.jpg", "a");
            TrashEntry fileDeletion = await FileService.DeleteFileAsync(shelf, EntryReference.ForPath("/photos/a.jpg"));
            await TrashDirectoryAsync(shelf, "/photos");

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
            Guid deletionId = await InsertDeletedDirectoryAsync(shelf, "photos", fileSizes: new long[] { 10 });
            Clock.Advance(Options.RestoreWindow + TimeSpan.FromSeconds(1));

            int purged = await TrashService.PurgeExpiredAsync(CancellationToken.None);

            Assert.Equal(1, purged);
            DirectoryRecord root = await Services.GetRequiredService<IDirectoryRepository>().GetRootAsync(shelf.Id);
            Assert.Empty(await Services.GetRequiredService<IDirectoryRepository>().GetPurgeableBeforeAsync(Clock.GetUtcNow(), 10));
            Assert.Null(await Services.GetRequiredService<IDirectoryRepository>().GetLiveChildAsync(root.Id, "photos"));
            await Assert.ThrowsAsync<NotFoundException>(() => TrashService.GetEntryAsync(shelf, deletionId));
        }

        [Fact]
        public async Task PurgeExpiredAsync_MoreThanOneBatch_PurgesEveryFile()
        {
            Shelf shelf = await CreateShelfAsync();
            int fileCount = ByteShelf.Server.Storage.Services.TrashService.PurgeBatchSize + 1;
            await InsertDeletedDirectoryAsync(shelf, "many", Enumerable.Repeat(1L, fileCount).ToArray());
            Clock.Advance(Options.RestoreWindow + TimeSpan.FromSeconds(1));

            int purged = await TrashService.PurgeExpiredAsync(CancellationToken.None);

            Assert.Equal(fileCount, purged);
        }

        /// <summary>
        /// Inserts a directory under the root, deleted as one deletion with a file of each size in it,
        /// the way a directory delete leaves the rows.
        /// </summary>
        private async Task<Guid> InsertDeletedDirectoryAsync(Shelf shelf, string name, long[] fileSizes)
        {
            IDirectoryRepository directories = Services.GetRequiredService<IDirectoryRepository>();
            IFileRepository files = Services.GetRequiredService<IFileRepository>();
            Guid deletionId = Guid.NewGuid();
            DateTimeOffset now = Clock.GetUtcNow();
            string sha256 = Sha256Of("x");

            await using IDbTransactionSession session = await TestRoot.SessionFactory.CreateSessionWithTransactionAsync();
            DirectoryRecord root = await directories.GetRootAsync(shelf.Id, session);
            DirectoryRecord directory = new DirectoryRecord
            {
                Id = Guid.NewGuid(),
                ShelfId = shelf.Id,
                ParentId = root.Id,
                Name = name,
                CreatedAt = now,
                UpdatedAt = now,
                DeletedAt = now,
                DeletionId = deletionId,
            };
            await directories.InsertAsync(directory, session);

            for (int i = 0; i < fileSizes.Length; i++)
            {
                await files.InsertAsync(
                    new FileRecord
                    {
                        Id = Guid.NewGuid(),
                        ShelfId = shelf.Id,
                        DirectoryId = directory.Id,
                        Name = $"file-{i}",
                        ContentType = ContentTypes.Default,
                        SizeBytes = fileSizes[i],
                        Sha256 = sha256,
                        CreatedAt = now,
                        UpdatedAt = now,
                        DeletedAt = now,
                        DeletionId = deletionId,
                    },
                    session);
            }

            await session.CommitAsync();
            return deletionId;
        }

        /// <summary>Marks a live directory deleted, as a directory delete does to its row.</summary>
        private async Task TrashDirectoryAsync(Shelf shelf, string path)
        {
            DirectoryRecord directory = (await Services.GetRequiredService<ShelfTree>().FindDirectoryAsync(shelf.Id, path))!;

            await using IDbSession session = await TestRoot.SessionFactory.CreateSessionAsync();
            using System.Data.Common.DbCommand command = session.Connection.CreateCommand();
            command.CommandText = "UPDATE directory SET deleted_at = $at, deletion_id = $deletion WHERE id = $id";
            AddParameter(command, "$at", ByteShelf.Server.Shared.Database.StoredInstant.ToText(Clock.GetUtcNow()));
            AddParameter(command, "$deletion", Guid.NewGuid().ToString("D"));
            AddParameter(command, "$id", directory.Id.ToString("D"));
            await command.ExecuteNonQueryAsync();
        }

        private static void AddParameter(System.Data.Common.DbCommand command, string name, object value)
        {
            System.Data.Common.DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }
    }
}
