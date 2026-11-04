using System.IO.Compression;
using System.Runtime.Versioning;
using ByteShelf.Server.Backup.Models;
using ByteShelf.Server.Backup.Services;
using ByteShelf.Server.ChangeLog.Models;
using ByteShelf.Server.Shared.IO;
using ByteShelf.Server.Shared.Root;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;
using EasyReasy.Database.Mapping;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using StoredObjects = ByteShelf.Server.Storage.Services.ObjectStore;

namespace ByteShelf.Server.Tests.Backup
{
    /// <summary>A backup of a root in use: the database as of one moment, the objects it names, the keys.</summary>
    public sealed class RootBackupTests : ServiceTestBase
    {
        private string _zipPath = null!;
        private string _restored = null!;

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            _zipPath = TestRoot.Root.Path + "-backup.zip";
            _restored = TestRoot.Root.Path + "-restored";
        }

        public override async Task DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            foreach (string file in BackupFiles())
            {
                File.Delete(file);
            }

            if (Directory.Exists(_restored))
            {
                Directory.Delete(_restored, recursive: true);
            }

            await base.DisposeAsync();
        }

        [Fact]
        public async Task WriteAsync_RootInUse_UnzipsToTheSameFilesObjectsAndKeys()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile a = await StoreTextAsync(shelf, "/a.txt", "first");
            ShelfFile b = await StoreTextAsync(shelf, "/b.txt", "second");
            await File.WriteAllTextAsync(Path.Combine(TestRoot.Root.KeysPath, "jwt-secret"), "secret");

            BackupSummary summary = await RootBackup.WriteAsync(TestRoot.Root, _zipPath, CancellationToken.None);

            RootFolder restored = Restore();
            Assert.Equal((2, (long)("first".Length + "second".Length)), (summary.ObjectCount, summary.ObjectBytes));
            StoredObjects objects = new StoredObjects(restored);
            Assert.Equal("first", await File.ReadAllTextAsync(objects.GetPath(a.Sha256)));
            Assert.Equal("second", await File.ReadAllTextAsync(objects.GetPath(b.Sha256)));
            Assert.Equal("secret", await File.ReadAllTextAsync(Path.Combine(restored.KeysPath, "jwt-secret")));
            Assert.Equal(2, await CountAsync(restored, "SELECT COUNT(*) FROM file"));
        }

        [Fact]
        public async Task WriteAsync_FileInTheTrash_IsKeptWithItsObject()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile trashed = await StoreTextAsync(shelf, "/trashed.txt", "restorable");
            await FileService.DeleteFileAsync(shelf, EntryReference.ForPath("/trashed.txt"));

            BackupSummary summary = await RootBackup.WriteAsync(TestRoot.Root, _zipPath, CancellationToken.None);

            RootFolder restored = Restore();
            Assert.Equal(1, summary.ObjectCount);
            Assert.True(new StoredObjects(restored).Exists(trashed.Sha256));
            Assert.Equal(1, await CountAsync(restored, "SELECT COUNT(*) FROM file WHERE deleted_at IS NOT NULL"));
        }

        [Fact]
        public async Task WriteAsync_TwoFilesWithTheSameContents_HoldTheObjectOnce()
        {
            Shelf shelf = await CreateShelfAsync();
            await StoreTextAsync(shelf, "/a.txt", "same");
            await StoreTextAsync(shelf, "/b.txt", "same");

            BackupSummary summary = await RootBackup.WriteAsync(TestRoot.Root, _zipPath, CancellationToken.None);

            Assert.Equal(1, summary.ObjectCount);
            using ZipArchive zip = ZipFile.OpenRead(_zipPath);
            Assert.Single(zip.Entries, entry => entry.FullName.StartsWith(RootFolder.ObjectsFolderName + "/", StringComparison.Ordinal));
        }

        [Fact]
        public async Task WriteAsync_UploadInProgress_IsLeftOut()
        {
            Shelf shelf = await CreateShelfAsync();
            await UploadService.StartAsync(shelf, new StartUploadRequest { Path = "/big.bin", SizeBytes = 5 }, Uploader);

            await RootBackup.WriteAsync(TestRoot.Root, _zipPath, CancellationToken.None);

            Assert.Equal(0, await CountAsync(Restore(), "SELECT COUNT(*) FROM upload"));
        }

        [Fact]
        public async Task WriteAsync_FilePurgedAfterTheSnapshot_IsPurgedInTheBackupTooAndLogged()
        {
            Shelf shelf = await CreateShelfAsync();
            await StoreTextAsync(shelf, "/kept.txt", "kept");
            ShelfFile purged = await StoreTextAsync(shelf, "/purged.txt", "purged");

            BackupSummary summary = await RootBackup.WriteAsync(TestRoot.Root, _zipPath, async () =>
            {
                await FileService.DeleteFileAsync(shelf, EntryReference.ForPath("/purged.txt"));
                // Emptying the trash removes the rows, then the object through the ObjectCollector.
                await TrashService.EmptyAsync(shelf, CancellationToken.None);
                Assert.False(ObjectStore.Exists(purged.Sha256));
            }, CancellationToken.None);

            RootFolder restored = Restore();
            Assert.Equal((1, 1), (summary.ObjectCount, summary.FilesPurgedMeanwhile));
            Assert.Equal(1, await CountAsync(restored, "SELECT COUNT(*) FROM file"));
            Assert.Equal(1, await CountAsync(restored, $"SELECT COUNT(*) FROM change_log WHERE entity_id = '{purged.Id}' AND action = '{ChangeAction.Purged}'"));
        }

        [Fact]
        public async Task WriteAsync_FileSwappedAfterTheSnapshot_IsBackedUpWithItsNewContentsAndLogged()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile plain = await StoreTextAsync(shelf, "/a.txt", "plain contents");
            // The server checks only that encrypted contents start as an age file.
            byte[] encrypted = System.Text.Encoding.ASCII.GetBytes("age-encryption.org/v1\nthe file");
            ShelfFile swapped = null!;

            BackupSummary summary = await RootBackup.WriteAsync(TestRoot.Root, _zipPath, async () =>
            {
                StartContentSwapRequest request = new StartContentSwapRequest { FileId = plain.Id, Sha256 = plain.Sha256, SizeBytes = encrypted.Length, Encrypted = true };
                swapped = await UploadService.SwapAsync(shelf, request, Uploader, new MemoryStream(encrypted), CancellationToken.None);
                Assert.False(ObjectStore.Exists(plain.Sha256));
            }, CancellationToken.None);

            RootFolder restored = Restore();
            Assert.Equal((1, 0), (summary.ObjectCount, summary.FilesPurgedMeanwhile));
            Assert.Equal(1, await CountAsync(restored, $"SELECT COUNT(*) FROM file WHERE id = '{plain.Id}' AND sha256 = '{swapped.Sha256}' AND encrypted = 1"));
            Assert.True(new StoredObjects(restored).Exists(swapped.Sha256));
            Assert.Equal(1, await CountAsync(restored, $"SELECT COUNT(*) FROM change_log WHERE entity_id = '{plain.Id}' AND action = '{ChangeAction.Swapped}'"));
        }

        [Fact]
        public async Task WriteAsync_TwoFilesSwappedToTheSameContentsAfterTheSnapshot_BacksUpThoseContentsOnce()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile first = await StoreTextAsync(shelf, "/a.txt", "first");
            ShelfFile second = await StoreTextAsync(shelf, "/b.txt", "second");
            byte[] encrypted = System.Text.Encoding.ASCII.GetBytes("age-encryption.org/v1\nthe same");
            string sameSha256 = null!;

            BackupSummary summary = await RootBackup.WriteAsync(TestRoot.Root, _zipPath, async () =>
            {
                foreach (ShelfFile plain in new[] { first, second })
                {
                    StartContentSwapRequest request = new StartContentSwapRequest { FileId = plain.Id, Sha256 = plain.Sha256, SizeBytes = encrypted.Length, Encrypted = true };
                    sameSha256 = (await UploadService.SwapAsync(shelf, request, Uploader, new MemoryStream(encrypted), CancellationToken.None)).Sha256;
                }
            }, CancellationToken.None);

            RootFolder restored = Restore();
            Assert.Equal(1, summary.ObjectCount);
            Assert.Equal(2, await CountAsync(restored, $"SELECT COUNT(*) FROM file WHERE sha256 = '{sameSha256}'"));
            Assert.True(new StoredObjects(restored).Exists(sameSha256));
        }

        [Fact]
        public async Task WriteAsync_ObjectMissingWhileAFilePointsAtIt_WritesNoBackup()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile lost = await StoreTextAsync(shelf, "/lost.txt", "lost");
            File.Delete(ObjectStore.GetPath(lost.Sha256));

            await Assert.ThrowsAsync<InvalidDataException>(() => RootBackup.WriteAsync(TestRoot.Root, _zipPath, CancellationToken.None));

            Assert.False(File.Exists(_zipPath));
        }

        [Fact]
        public async Task WriteAsync_ObjectThatNoLongerMatchesItsHash_WritesNothingAndKeepsTheBackupBefore()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "the real bytes");
            await File.WriteAllTextAsync(ObjectStore.GetPath(file.Sha256), "damaged");
            await File.WriteAllTextAsync(_zipPath, "the backup before");

            await Assert.ThrowsAsync<InvalidDataException>(() => RootBackup.WriteAsync(TestRoot.Root, _zipPath, CancellationToken.None));

            Assert.Equal("the backup before", await File.ReadAllTextAsync(_zipPath));
            Assert.Equal(new[] { _zipPath }, BackupFiles());
        }

        [Fact]
        public async Task WriteAsync_BackupThere_IsReplaced()
        {
            Shelf shelf = await CreateShelfAsync();
            await StoreTextAsync(shelf, "/a.txt", "first");
            await File.WriteAllTextAsync(_zipPath, "the backup before");

            await RootBackup.WriteAsync(TestRoot.Root, _zipPath, CancellationToken.None);

            Assert.Equal(1, await CountAsync(Restore(), "SELECT COUNT(*) FROM file"));
        }

        [UnixFact]
        [UnsupportedOSPlatform("windows")]
        public async Task WriteAsync_ZipFilesBesideItAndTheDatabaseAndKeysInIt_AreOwnerOnly()
        {
            await File.WriteAllTextAsync(Path.Combine(TestRoot.Root.KeysPath, "jwt-secret"), "secret");
            Dictionary<string, UnixFileMode> besideTheZip = new Dictionary<string, UnixFileMode>();

            await RootBackup.WriteAsync(TestRoot.Root, _zipPath, () =>
            {
                // The snapshot among them: it holds password hashes.
                foreach (string file in BackupFiles())
                {
                    besideTheZip[Path.GetFileName(file)] = File.GetUnixFileMode(file);
                }

                return Task.CompletedTask;
            }, CancellationToken.None);

            Assert.NotEmpty(besideTheZip);
            Assert.All(besideTheZip.Values, mode => Assert.Equal(OwnerOnly.FilePermissions, mode));
            Assert.Equal(OwnerOnly.FilePermissions, File.GetUnixFileMode(_zipPath));
            using ZipArchive zip = ZipFile.OpenRead(_zipPath);
            Assert.Equal((int)OwnerOnly.FilePermissions, zip.GetEntry(RootFolder.DatabaseFileName)!.ExternalAttributes >> 16);
            Assert.Equal((int)OwnerOnly.FilePermissions, zip.GetEntry($"{RootFolder.KeysFolderName}/jwt-secret")!.ExternalAttributes >> 16);
        }

        [Fact]
        public async Task WriteAsync_WhileAnotherBackupToThePathRuns_FailsAndLeavesThatOneWhole()
        {
            Shelf shelf = await CreateShelfAsync();
            await StoreTextAsync(shelf, "/a.txt", "first");
            Exception? second = null;

            await RootBackup.WriteAsync(TestRoot.Root, _zipPath, async () =>
            {
                second = await Record.ExceptionAsync(() => RootBackup.WriteAsync(TestRoot.Root, _zipPath, CancellationToken.None));
            }, CancellationToken.None);

            Assert.IsType<IOException>(second);
            Assert.Equal(1, await CountAsync(Restore(), "SELECT COUNT(*) FROM file"));
            Assert.Equal(new[] { _zipPath }, BackupFiles());
        }

        [Fact]
        public async Task WriteAsync_InsideTheRoot_IsRefused()
        {
            await Assert.ThrowsAsync<ArgumentException>(() => RootBackup.WriteAsync(TestRoot.Root, Path.Combine(TestRoot.Root.Path, "backup.zip"), CancellationToken.None));
        }

        [Theory]
        [InlineData("root", null)]
        [InlineData("root", "--other")]
        [InlineData(null, "backup.zip")]
        public void Run_WithoutAZipPathOrRoot_IsUsage(string? root, string? zipPath)
        {
            // Usage is answered before either path is touched.
            Assert.Equal(BackupCommand.Usage, BackupCommand.Run(root, zipPath, NullLogger.Instance));
        }

        [Fact]
        public void Run_FolderWithoutADatabase_FailsAndCreatesNothing()
        {
            Directory.CreateDirectory(_restored);

            Assert.Equal(BackupCommand.Failed, BackupCommand.Run(_restored, _zipPath, NullLogger.Instance));

            Assert.Empty(Directory.EnumerateFileSystemEntries(_restored));
            Assert.False(File.Exists(_zipPath));
        }

        [Fact]
        public async Task Run_DamagedObject_Fails()
        {
            Shelf shelf = await CreateShelfAsync();
            ShelfFile file = await StoreTextAsync(shelf, "/a.txt", "the real bytes");
            await File.WriteAllTextAsync(ObjectStore.GetPath(file.Sha256), "damaged");

            Assert.Equal(BackupCommand.Failed, BackupCommand.Run(TestRoot.Root.Path, _zipPath, NullLogger.Instance));
        }

        [Fact]
        public void Run_Root_WritesTheZip()
        {
            Assert.Equal(BackupCommand.Written, BackupCommand.Run(TestRoot.Root.Path, _zipPath, NullLogger.Instance));

            Assert.True(File.Exists(_zipPath));
        }

        /// <summary>The zip and every file the backup keeps beside it while it runs.</summary>
        private string[] BackupFiles()
        {
            return Directory.GetFiles(Path.GetDirectoryName(_zipPath)!, Path.GetFileName(_zipPath) + "*");
        }

        private RootFolder Restore()
        {
            ZipFile.ExtractToDirectory(_zipPath, _restored);
            return RootFolder.OpenExisting(_restored);
        }

        private static async Task<long> CountAsync(RootFolder root, string sql)
        {
            await using SqliteConnection connection = new SqliteConnection(RootFolder.ConnectionStringFor(root.DatabasePath, pooling: false));
            await connection.OpenAsync();
            return await connection.QuerySingleAsync<long>(sql);
        }
    }
}
