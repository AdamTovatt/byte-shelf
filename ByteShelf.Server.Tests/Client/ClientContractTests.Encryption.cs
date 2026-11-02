using System.Text;
using ByteShelf.Tools;
using ByteShelfClient;
using ByteShelfClient.Encryption;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.Client
{
    public abstract partial class ClientContractTests
    {
        private const string FolderPasswordText = "a folder's password";

        [Fact]
        public async Task EncryptDirectoryAsync_MarksItAndEverythingUnderItAsEncryptedByIt()
        {
            await Shelf.CreateDirectoryAsync("/secret/inner");

            ShelfDirectory encrypted = await Shelf.EncryptDirectoryAsync("/secret", await CheckAsync());

            DirectoryListing inner = await Shelf.ListAsync("/secret/inner");
            PathEncryption deep = await Shelf.GetEncryptionAsync("/secret/inner/not/yet/a.txt");
            PathEncryption outside = await Shelf.GetEncryptionAsync("/elsewhere/a.txt");
            Assert.True(encrypted.Encrypted);
            Assert.True(Assert.Single((await Shelf.ListAsync()).Directories).Directory.Encrypted);
            Assert.Equal(("/secret", "/secret"), (inner.EncryptedBy?.Directory.Path, deep.EncryptedBy?.Directory.Path));
            Assert.Null(outside.EncryptedBy);
        }

        [Fact]
        public async Task GetEncryptionAsync_UnderAnEncryptedDirectory_AnswersTheCheckItsPasswordMatches()
        {
            await Shelf.CreateDirectoryAsync("/secret");
            await Shelf.EncryptDirectoryAsync("/secret", await CheckAsync());

            EncryptedFolder folder = (await Shelf.GetEncryptionAsync("/secret/a.txt")).EncryptedBy!;

            Assert.True(await FolderPassword.MatchesAsync(folder, FolderPasswordText));
            Assert.False(await FolderPassword.MatchesAsync(folder, "another password"));
        }

        [Fact]
        public async Task UploadAsync_PlainFileUnderAnEncryptedDirectory_ThrowsConflict()
        {
            await Shelf.CreateDirectoryAsync("/secret");
            await Shelf.EncryptDirectoryAsync("/secret", await CheckAsync());

            ByteShelfException exception = await RefusalOf(UploadTextAsync("/secret/deeper/a.txt", "plain"));

            Assert.Equal(ErrorCodes.Conflict, exception.Code);
        }

        [Fact]
        public async Task UploadAsync_EncryptedFileUnderAnEncryptedDirectory_StoresItMarkedEncrypted()
        {
            await Shelf.CreateDirectoryAsync("/secret");
            await Shelf.EncryptDirectoryAsync("/secret", await CheckAsync());

            ShelfFile stored = await UploadEncryptedAsync("/secret/a.pdf", "contents");

            Assert.Equal((true, "application/pdf"), (stored.Encrypted, stored.ContentType));
            Assert.True(Assert.Single((await Shelf.ListAsync("/secret")).Files).Encrypted);
        }

        [Fact]
        public async Task UploadAsync_MarkedEncryptedButNoAgeFile_ThrowsInvalidRequest()
        {
            ByteShelfException exception = await RefusalOf(
                Shelf.UploadAsync("/a.txt", new MemoryStream(Encoding.UTF8.GetBytes("plain")), new UploadOptions { Encrypted = true }));

            Assert.Equal(ErrorCodes.InvalidRequest, exception.Code);
        }

        [Theory]
        [InlineData("/secret/inner")]
        [InlineData("/")]
        [InlineData("/secret")]
        public async Task EncryptDirectoryAsync_InsideAboveOrAtAnEncryptedOne_ThrowsConflict(string path)
        {
            await Shelf.CreateDirectoryAsync("/secret/inner");
            await Shelf.EncryptDirectoryAsync("/secret", await CheckAsync());

            ByteShelfException exception = await RefusalOf(Shelf.EncryptDirectoryAsync(path, await CheckAsync()));

            Assert.Equal(ErrorCodes.Conflict, exception.Code);
        }

        [Fact]
        public async Task EncryptDirectoryAsync_CheckNotAnAgeFile_ThrowsInvalidRequest()
        {
            await Shelf.CreateDirectoryAsync("/secret");

            ByteShelfException exception = await RefusalOf(Shelf.EncryptDirectoryAsync("/secret", Convert.ToBase64String(Encoding.UTF8.GetBytes("not age"))));

            Assert.Equal(ErrorCodes.InvalidRequest, exception.Code);
        }

        [Theory]
        [InlineData("not base64!")]
        [InlineData("long")]
        public async Task EncryptDirectoryAsync_CheckNotBase64OrTooLong_ThrowsInvalidRequest(string check)
        {
            await Shelf.CreateDirectoryAsync("/secret");
            string tooLong = Convert.ToBase64String([.. FileEncryption.AgeHeader, .. new byte[FileEncryption.MaxCheckBytes]]);

            ByteShelfException exception = await RefusalOf(Shelf.EncryptDirectoryAsync("/secret", check == "long" ? tooLong : check));

            Assert.Equal(ErrorCodes.InvalidRequest, exception.Code);
        }

        [Fact]
        public async Task UploadAsync_PlainOverAnEncryptedFileOutsideEncryptedDirectories_StoresItPlain()
        {
            await UploadEncryptedAsync("/a.txt", "secret");

            ShelfFile stored = await Shelf.UploadAsync("/a.txt", new MemoryStream(Encoding.UTF8.GetBytes("plain")), new UploadOptions { Overwrite = true });

            Assert.False(stored.Encrypted);
        }

        [Fact]
        public async Task MoveDirectoryAsync_PlainOrHoldingAnEncryptedOneIntoAnEncryptedDirectory_ThrowsConflict()
        {
            await Shelf.CreateDirectoryAsync("/secret");
            await Shelf.CreateDirectoryAsync("/plain");
            await Shelf.CreateDirectoryAsync("/holding/own");
            await Shelf.EncryptDirectoryAsync("/secret", await CheckAsync());
            await Shelf.EncryptDirectoryAsync("/holding/own", await CheckAsync());

            ByteShelfException plain = await RefusalOf(Shelf.MoveDirectoryAsync("/plain", "/secret/plain"));
            ByteShelfException holding = await RefusalOf(Shelf.MoveDirectoryAsync("/holding", "/secret/holding"));

            Assert.Equal((ErrorCodes.Conflict, ErrorCodes.Conflict), (plain.Code, holding.Code));
        }

        [Fact]
        public async Task MoveDirectoryAsync_WithinAnEncryptedDirectory_MovesIt()
        {
            await Shelf.CreateDirectoryAsync("/secret/a");
            await Shelf.EncryptDirectoryAsync("/secret", await CheckAsync());

            ShelfDirectory moved = await Shelf.MoveDirectoryAsync("/secret/a", "/secret/b/a");

            Assert.Equal("/secret/b/a", moved.Path);
        }

        [Fact]
        public async Task RestoreAsync_AnEncryptedDirectoryIntoOneEncryptedSince_ThrowsConflict()
        {
            await Shelf.CreateDirectoryAsync("/outer/inner");
            await Shelf.EncryptDirectoryAsync("/outer/inner", await CheckAsync());
            TrashEntry deleted = await Shelf.DeleteDirectoryAsync("/outer/inner");
            await Shelf.EncryptDirectoryAsync("/outer", await CheckAsync());

            ByteShelfException exception = await RefusalOf(Shelf.RestoreAsync(deleted.DeletionId));

            Assert.Equal(ErrorCodes.Conflict, exception.Code);
        }

        [Fact]
        public async Task RestoreAsync_AnEncryptedFileIntoItsEncryptedDirectory_RestoresIt()
        {
            await Shelf.CreateDirectoryAsync("/secret");
            await Shelf.EncryptDirectoryAsync("/secret", await CheckAsync());
            await UploadEncryptedAsync("/secret/a.txt", "a");
            TrashEntry deleted = await Shelf.DeleteFileAsync("/secret/a.txt");

            await Shelf.RestoreAsync(deleted.DeletionId);

            Assert.True((await Shelf.GetFileAsync("/secret/a.txt")).Encrypted);
        }

        [Fact]
        public async Task MoveFileAsync_IntoAnEncryptedDirectoryFromOutside_ThrowsConflictEvenEncrypted()
        {
            await Shelf.CreateDirectoryAsync("/secret");
            await Shelf.EncryptDirectoryAsync("/secret", await CheckAsync());
            await UploadTextAsync("/plain.txt", "plain");
            await UploadEncryptedAsync("/own.txt", "its own password");

            ByteShelfException plain = await RefusalOf(Shelf.MoveFileAsync("/plain.txt", "/secret/plain.txt"));
            ByteShelfException encrypted = await RefusalOf(Shelf.MoveFileAsync("/own.txt", "/secret/own.txt"));

            Assert.Equal((ErrorCodes.Conflict, ErrorCodes.Conflict), (plain.Code, encrypted.Code));
        }

        [Fact]
        public async Task MoveFileAsync_WithinAndOutOfAnEncryptedDirectory_MovesItStillEncrypted()
        {
            await Shelf.CreateDirectoryAsync("/secret");
            await Shelf.EncryptDirectoryAsync("/secret", await CheckAsync());
            await UploadEncryptedAsync("/secret/a.txt", "a");

            ShelfFile within = await Shelf.MoveFileAsync("/secret/a.txt", "/secret/sub/a.txt");
            ShelfFile outside = await Shelf.MoveFileAsync("/secret/sub/a.txt", "/out/a.txt");

            Assert.Equal(("/secret/sub/a.txt", "/out/a.txt", true), (within.Path, outside.Path, outside.Encrypted));
        }

        [Fact]
        public async Task MoveDirectoryAsync_AnEncryptedOneIntoAnother_ThrowsConflict()
        {
            await Shelf.CreateDirectoryAsync("/one");
            await Shelf.CreateDirectoryAsync("/two");
            await Shelf.EncryptDirectoryAsync("/one", await CheckAsync());
            await Shelf.EncryptDirectoryAsync("/two", await CheckAsync());

            ByteShelfException exception = await RefusalOf(Shelf.MoveDirectoryAsync("/two", "/one/two"));

            Assert.Equal(ErrorCodes.Conflict, exception.Code);
        }

        [Fact]
        public async Task RestoreAsync_PlainFileIntoADirectoryEncryptedSince_ThrowsConflict()
        {
            await UploadTextAsync("/later/a.txt", "plain");
            TrashEntry deleted = await Shelf.DeleteFileAsync("/later/a.txt");
            await Shelf.EncryptDirectoryAsync("/later", await CheckAsync());

            ByteShelfException exception = await RefusalOf(Shelf.RestoreAsync(deleted.DeletionId));

            Assert.Equal(ErrorCodes.Conflict, exception.Code);
        }

        [Fact]
        public async Task CreateLinks_ForEncryptedFilesAndPlaces_ThrowConflict()
        {
            await Shelf.CreateDirectoryAsync("/secret");
            await Shelf.EncryptDirectoryAsync("/secret", await CheckAsync());
            await UploadEncryptedAsync("/own.txt", "its own password");

            ByteShelfException share = await RefusalOf(Shelf.CreateDownloadLinkAsync("/own.txt"));
            ByteShelfException underFolder = await RefusalOf(Shelf.CreateUploadLinkAsync("/secret/new.txt"));
            ByteShelfException overFile = await RefusalOf(Shelf.CreateUploadLinkAsync("/own.txt", new UploadLinkOptions { Overwrite = true }));

            Assert.Equal((ErrorCodes.Conflict, ErrorCodes.Conflict, ErrorCodes.Conflict), (share.Code, underFolder.Code, overFile.Code));
        }

        [Fact]
        public async Task RemoveDirectoryEncryptionAsync_LetsPlainFilesInAndLeavesTheEncryptedOnesSo()
        {
            await Shelf.CreateDirectoryAsync("/secret");
            await Shelf.EncryptDirectoryAsync("/secret", await CheckAsync());
            await UploadEncryptedAsync("/secret/a.txt", "a");

            ShelfDirectory plain = await Shelf.RemoveDirectoryEncryptionAsync("/secret");
            await UploadTextAsync("/secret/b.txt", "plain now");

            Assert.False(plain.Encrypted);
            Assert.Null((await Shelf.ListAsync("/secret")).EncryptedBy);
            Assert.Equal(new[] { ("a.txt", true), ("b.txt", false) }, (await Shelf.ListAsync("/secret")).Files.Select(file => (file.Name, file.Encrypted)));
        }

        [Fact]
        public async Task ListAsync_CountsTheEncryptedFilesUnderEachDirectoryAtAnyDepth()
        {
            await UploadTextAsync("/mixed/plain.txt", "plain");
            await UploadEncryptedAsync("/mixed/deeper/a.txt", "a");
            await UploadEncryptedAsync("/mixed/deeper/b.txt", "b");
            await UploadTextAsync("/other/plain.txt", "plain");

            DirectoryListing root = await Shelf.ListAsync();

            Assert.Equal(
                new[] { ("mixed", 3, 2), ("other", 1, 0) },
                root.Directories.Select(listed => (listed.Directory.Name, listed.FileCount, listed.EncryptedFileCount)));
        }

        [Fact]
        public async Task RemoveDirectoryEncryptionAsync_NotEncrypted_ThrowsConflict()
        {
            await Shelf.CreateDirectoryAsync("/plain");

            ByteShelfException exception = await RefusalOf(Shelf.RemoveDirectoryEncryptionAsync("/plain"));

            Assert.Equal(ErrorCodes.Conflict, exception.Code);
        }

        /// <summary>A folder check for <see cref="FolderPasswordText"/>, at a low work factor so the tests run fast.</summary>
        private static Task<string> CheckAsync()
        {
            return FolderPassword.CreateCheckAsync(FolderPasswordText, CancellationToken.None, workFactor: 10);
        }

        /// <summary>Stores <paramref name="text"/> encrypted with <see cref="FolderPasswordText"/>, as the CLI does, at a low work factor so the tests run fast.</summary>
        private Task<ShelfFile> UploadEncryptedAsync(string path, string text)
        {
            return Transfers.UploadAsync(Shelf, path, new MemoryStream(Encoding.UTF8.GetBytes(text)), new UploadOptions(), new Encryptor(FolderPasswordText, 10), CancellationToken.None);
        }
    }
}
