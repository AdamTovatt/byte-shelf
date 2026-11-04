using System.Security.Cryptography;
using System.Text;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfClient;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.Client
{
    /// <summary>
    /// What every <see cref="IByteShelfClient"/> does the same way, run once against the real server
    /// over HTTP and once against the in-memory client, so an app tested with the in-memory one meets
    /// the same rules in production.
    /// </summary>
    public abstract partial class ClientContractTests : IAsyncLifetime
    {
        /// <summary>The password of the admin the client acts as.</summary>
        protected const string AdminPassword = ServerTestHost.Password;

        /// <summary>Longer than the HTTP client's single-request limit in these tests, so it is sent in chunks.</summary>
        protected const string LongText = "0123456789abcdefghijklmnopqrstuvwxyz0123456789ABCD";

        protected IByteShelfClient Client { get; private set; } = null!;

        protected IShelfClient Shelf => Client.Shelf("docs");

        public virtual async Task InitializeAsync()
        {
            Client = await CreateAdminClientAsync();
            await Client.CreateShelfAsync("docs");
        }

        public virtual Task DisposeAsync()
        {
            return Task.CompletedTask;
        }

        [Fact]
        public async Task UploadAsync_SmallFile_StoresItsBytesAndRecord()
        {
            ShelfFile stored = await UploadTextAsync("/notes/a.txt", "hello", contentType: "text/plain");

            Assert.Equal(("/notes/a.txt", "a.txt", 5L, Sha256Of("hello"), "text/plain"), (stored.Path, stored.Name, stored.SizeBytes, stored.Sha256, stored.ContentType));
            Assert.Equal("hello", await ReadTextAsync("/notes/a.txt"));
        }

        [Fact]
        public async Task UploadAsync_LongFile_StoresEveryByte()
        {
            ShelfFile stored = await UploadTextAsync("/long.txt", LongText);

            Assert.Equal(Sha256Of(LongText), stored.Sha256);
            Assert.Equal(LongText, await ReadTextAsync("/long.txt"));
        }

        [Fact]
        public async Task UploadAsync_StreamThatCannotSeek_StoresIt()
        {
            await using UnseekableStream content = new UnseekableStream(Encoding.UTF8.GetBytes(LongText));

            await Shelf.UploadAsync("/piped.txt", content);

            Assert.Equal(LongText, await ReadTextAsync("/piped.txt"));
        }

        [Theory]
        [InlineData("skip-hello")]
        [InlineData("skip-" + LongText)]
        public async Task UploadAsync_StreamPastItsStart_StoresFromItsPosition(string text)
        {
            using MemoryStream content = new MemoryStream(Encoding.UTF8.GetBytes(text)) { Position = 5 };

            await Shelf.UploadAsync("/a.txt", content);

            Assert.Equal(text[5..], await ReadTextAsync("/a.txt"));
        }

        [Fact]
        public async Task UploadAsync_PathTaken_ThrowsConflictAndKeepsTheFile()
        {
            await UploadTextAsync("/a.txt", "old");

            ByteShelfException exception = await RefusalOf(UploadTextAsync("/a.txt", "new"));

            Assert.Equal(ErrorCodes.Conflict, exception.Code);
            Assert.Equal("old", await ReadTextAsync("/a.txt"));
        }

        [Fact]
        public async Task UploadAsync_Overwrite_ReplacesTheFileAndTrashesTheOldOne()
        {
            ShelfFile old = await UploadTextAsync("/a.txt", "old");

            await UploadTextAsync("/a.txt", "new", overwrite: true);

            Assert.Equal("new", await ReadTextAsync("/a.txt"));
            TrashEntry trashed = Assert.Single(await Shelf.GetTrashAsync());
            Assert.Equal((TrashEntryKind.File, old.Id, "/a.txt"), (trashed.Kind, trashed.EntryId, trashed.Path));
        }

        [Fact]
        public async Task UploadAsync_OverQuota_ThrowsQuotaExceeded()
        {
            await Client.CreateShelfAsync("small", quotaBytes: 4);

            ByteShelfException exception = await RefusalOf(Client.Shelf("small").UploadAsync("/a.txt", new MemoryStream(Encoding.UTF8.GetBytes("12345"))));

            Assert.Equal(ErrorCodes.QuotaExceeded, exception.Code);
        }

        [Theory]
        [InlineData("/q3.PDF", "application/pdf")]
        [InlineData("/notes.unknown", ContentTypes.Default)]
        public async Task UploadAsync_NoContentType_IsTypedByTheExtension(string path, string expected)
        {
            ShelfFile stored = await UploadTextAsync(path, "a");

            Assert.Equal(expected, stored.ContentType);
        }

        [Fact]
        public async Task UploadAsync_ContentTypeNotAMediaType_ThrowsArgumentException()
        {
            await Assert.ThrowsAsync<ArgumentException>(() => UploadTextAsync("/a.txt", "a", contentType: "not a type"));
        }

        [Fact]
        public async Task UploadAsync_PathNotAllowed_ThrowsArgumentException()
        {
            await Assert.ThrowsAsync<ArgumentException>(() => UploadTextAsync("/a/../b.txt", "a"));
        }

        [Fact]
        public async Task GetFileAsync_PathWithCharactersAQueryEscapes_FindsTheFile()
        {
            await UploadTextAsync("/q&a #1/ä + ö?.txt", "a");

            ShelfFile found = await Shelf.GetFileAsync("/q&a #1/ä + ö?.txt");

            Assert.Equal("ä + ö?.txt", found.Name);
        }

        [Fact]
        public async Task GetFileAsync_ById_AnswersTheSameAsByPath()
        {
            ShelfFile stored = await UploadTextAsync("/a.txt", "a");

            ShelfFile byId = await Shelf.GetFileAsync(stored.Id);

            Assert.Equal((stored.Path, stored.Sha256), (byId.Path, byId.Sha256));
        }

        [Fact]
        public async Task GetFileAsync_Missing_ThrowsNotFound()
        {
            ByteShelfException exception = await RefusalOf(Shelf.GetFileAsync("/missing.txt"));

            Assert.Equal(ErrorCodes.NotFound, exception.Code);
        }

        [Fact]
        public async Task ListAsync_Root_AnswersDirectoriesAndFilesByName()
        {
            await UploadTextAsync("/b.txt", "b");
            await UploadTextAsync("/a.txt", "a");
            await Shelf.CreateDirectoryAsync("/z");
            await Shelf.CreateDirectoryAsync("/y");

            DirectoryListing listing = await Shelf.ListAsync();

            Assert.Equal("/", listing.Directory.Path);
            Assert.Equal(new[] { "y", "z" }, listing.Directories.Select(listed => listed.Directory.Name));
            Assert.Equal(new[] { "a.txt", "b.txt" }, listing.Files.Select(file => file.Name));
        }

        [Fact]
        public async Task ListAsync_Subdirectory_AnswersWhatIsInIt()
        {
            await UploadTextAsync("/a/inner.txt", "i");
            await Shelf.CreateDirectoryAsync("/a/b");
            await UploadTextAsync("/outer.txt", "o");

            DirectoryListing listing = await Shelf.ListAsync("/a");

            Assert.Equal("/a", listing.Directory.Path);
            Assert.Equal(new[] { "b" }, listing.Directories.Select(listed => listed.Directory.Name));
            Assert.Equal(new[] { "inner.txt" }, listing.Files.Select(file => file.Name));
        }

        [Fact]
        public async Task ListAsync_Directories_CountTheLiveFilesUnderThemAtAnyDepth()
        {
            await UploadTextAsync("/a/one.txt", "abc");
            await UploadTextAsync("/a/b/c/two.txt", "de");
            await UploadTextAsync("/a/b/gone.txt", "trashed");
            await UploadTextAsync("/a/old/gone.txt", "trashed too");
            await Shelf.DeleteFileAsync("/a/b/gone.txt");
            await Shelf.DeleteDirectoryAsync("/a/old");
            await UploadTextAsync("/z/three.txt", "f");
            await Shelf.CreateDirectoryAsync("/empty");
            await UploadTextAsync("/top.txt", "not under a directory");

            DirectoryListing listing = await Shelf.ListAsync();

            Assert.Equal(
                new[] { ("a", 5L, 2), ("empty", 0L, 0), ("z", 1L, 1) },
                listing.Directories.Select(listed => (listed.Directory.Name, listed.SizeBytes, listed.FileCount)));
        }

        [Fact]
        public async Task CreateDirectoryAsync_Existing_AnswersIt()
        {
            ShelfDirectory first = await Shelf.CreateDirectoryAsync("/a/b");

            ShelfDirectory second = await Shelf.CreateDirectoryAsync("/a/b");

            Assert.Equal(first.Id, second.Id);
        }

        [Fact]
        public async Task CreateDirectoryAsync_UnderAFile_ThrowsConflict()
        {
            await UploadTextAsync("/a", "a");

            ByteShelfException exception = await RefusalOf(Shelf.CreateDirectoryAsync("/a/b"));

            Assert.Equal(ErrorCodes.Conflict, exception.Code);
        }

        [Fact]
        public async Task MoveFileAsync_ToANewPath_LeavesNothingAtTheOldOne()
        {
            ShelfFile stored = await UploadTextAsync("/a.txt", "a");

            ShelfFile moved = await Shelf.MoveFileAsync("/a.txt", "/archive/b.txt");

            Assert.Equal((stored.Id, "/archive/b.txt"), (moved.Id, moved.Path));
            Assert.Equal(ErrorCodes.NotFound, (await RefusalOf(Shelf.GetFileAsync("/a.txt"))).Code);
        }

        [Fact]
        public async Task MoveFileAsync_OntoATakenPath_ThrowsConflict()
        {
            await UploadTextAsync("/a.txt", "a");
            await UploadTextAsync("/b.txt", "b");

            ByteShelfException exception = await RefusalOf(Shelf.MoveFileAsync("/a.txt", "/b.txt"));

            Assert.Equal(ErrorCodes.Conflict, exception.Code);
        }

        [Fact]
        public async Task MoveDirectoryAsync_MovesEverythingUnderIt()
        {
            await UploadTextAsync("/photos/2026/a.jpg", "a");

            await Shelf.MoveDirectoryAsync("/photos", "/archive/photos");

            Assert.Equal("/archive/photos/2026/a.jpg", (await Shelf.GetFileAsync("/archive/photos/2026/a.jpg")).Path);
        }

        [Fact]
        public async Task MoveDirectoryAsync_IntoItself_ThrowsInvalidRequest()
        {
            await Shelf.CreateDirectoryAsync("/a");

            ByteShelfException exception = await RefusalOf(Shelf.MoveDirectoryAsync("/a", "/a/b"));

            Assert.Equal(ErrorCodes.InvalidRequest, exception.Code);
        }

        [Fact]
        public async Task DeleteFileAsync_ThenRestore_PutsItBack()
        {
            await UploadTextAsync("/a.txt", "a");

            TrashEntry deleted = await Shelf.DeleteFileAsync("/a.txt");
            await Shelf.RestoreAsync(deleted.DeletionId);

            Assert.Equal("a", await ReadTextAsync("/a.txt"));
            Assert.Empty(await Shelf.GetTrashAsync());
        }

        [Fact]
        public async Task DeleteDirectoryAsync_TrashesEverythingUnderItAsOneDeletion()
        {
            await UploadTextAsync("/photos/a.jpg", "aa");
            await UploadTextAsync("/photos/2026/b.jpg", "bbb");

            TrashEntry deleted = await Shelf.DeleteDirectoryAsync("/photos");

            Assert.Equal((TrashEntryKind.Directory, "/photos", 2, 5L), (deleted.Kind, deleted.Path, deleted.FileCount, deleted.SizeBytes));
            Assert.Empty((await Shelf.ListAsync()).Directories);
        }

        [Fact]
        public async Task RestoreAsync_NameTakenMeanwhile_ThrowsConflict()
        {
            await UploadTextAsync("/a.txt", "old");
            TrashEntry deleted = await Shelf.DeleteFileAsync("/a.txt");
            await UploadTextAsync("/a.txt", "new");

            ByteShelfException exception = await RefusalOf(Shelf.RestoreAsync(deleted.DeletionId));

            Assert.Equal(ErrorCodes.Conflict, exception.Code);
        }

        [Fact]
        public async Task RestoreAsync_DirectoryItWasInIsInTheTrash_ThrowsConflict()
        {
            await UploadTextAsync("/a/b.txt", "b");
            TrashEntry file = await Shelf.DeleteFileAsync("/a/b.txt");
            await Shelf.DeleteDirectoryAsync("/a");

            ByteShelfException exception = await RefusalOf(Shelf.RestoreAsync(file.DeletionId));

            Assert.Equal(ErrorCodes.Conflict, exception.Code);
        }

        [Fact]
        public async Task DeleteDirectoryAsync_WhatWasAlreadyInTheTrash_StaysInItsOwnDeletion()
        {
            await UploadTextAsync("/a/x.txt", "x");
            await UploadTextAsync("/a/y.txt", "y");
            TrashEntry file = await Shelf.DeleteFileAsync("/a/x.txt");

            TrashEntry directory = await Shelf.DeleteDirectoryAsync("/a");
            await Shelf.RestoreAsync(directory.DeletionId);

            Assert.Equal(1, directory.FileCount);
            Assert.Equal(new[] { "y.txt" }, (await Shelf.ListAsync("/a")).Files.Select(listed => listed.Name));
            Assert.Equal(file.DeletionId, Assert.Single(await Shelf.GetTrashAsync()).DeletionId);
        }

        [Fact]
        public async Task EmptyTrashAsync_FreesWhatTheTrashHeld()
        {
            await UploadTextAsync("/a.txt", "abc");
            await Shelf.DeleteFileAsync("/a.txt");

            await Shelf.EmptyTrashAsync();

            Assert.Empty(await Shelf.GetTrashAsync());
            Assert.Equal(0, (await Shelf.GetInfoAsync()).UsedBytes);
        }

        [Fact]
        public async Task GetInfoAsync_CountsTheTrashAsUsedButNotAsFiles()
        {
            await UploadTextAsync("/a.txt", "abc");
            await UploadTextAsync("/b.txt", "de");
            await Shelf.DeleteFileAsync("/b.txt");

            ShelfInfo info = await Shelf.GetInfoAsync();

            Assert.Equal(("docs", 5L, 1), (info.Name, info.UsedBytes, info.FileCount));
        }

        [Fact]
        public async Task CreateShelfAsync_NameTaken_ThrowsConflict()
        {
            ByteShelfException exception = await RefusalOf(Client.CreateShelfAsync("docs"));

            Assert.Equal(ErrorCodes.Conflict, exception.Code);
        }

        [Fact]
        public async Task GetShelvesAsync_ListsEveryShelfByName()
        {
            await Client.CreateShelfAsync("backups");

            IReadOnlyList<ShelfInfo> shelves = await Client.GetShelvesAsync();

            Assert.Equal(new[] { "backups", "docs" }, shelves.Select(shelf => shelf.Name));
        }

        [Fact]
        public async Task UpdateAsync_NewName_MovesTheShelfToIt()
        {
            await UploadTextAsync("/a.txt", "a");

            await Shelf.UpdateAsync("papers", quotaBytes: 100);

            Assert.Equal(100, (await Client.Shelf("papers").GetInfoAsync()).QuotaBytes);
            Assert.Equal(ErrorCodes.NotFound, (await RefusalOf(Shelf.GetInfoAsync())).Code);
        }

        [Fact]
        public async Task DeleteAsync_ShelfWithATrashedFile_ThrowsConflict()
        {
            await UploadTextAsync("/a.txt", "a");
            await Shelf.DeleteFileAsync("/a.txt");

            ByteShelfException exception = await RefusalOf(Shelf.DeleteAsync());

            Assert.Equal(ErrorCodes.Conflict, exception.Code);
        }

        [Fact]
        public async Task DeleteAsync_EmptyShelf_RemovesIt()
        {
            await Shelf.DeleteAsync();

            Assert.Empty(await Client.GetShelvesAsync());
        }

        [Fact]
        public async Task CreateUserAsync_EmailTakenInAnotherCase_ThrowsConflict()
        {
            await CreateUserAsync("user@example.com");

            ByteShelfException exception = await RefusalOf(CreateUserAsync("USER@example.com"));

            Assert.Equal(ErrorCodes.Conflict, exception.Code);
        }

        [Fact]
        public async Task CreateUserAsync_ThenGetUsers_ListsThemLastWithoutRoles()
        {
            UserInfo created = await CreateUserAsync("user@example.com");

            IReadOnlyList<UserInfo> users = await Client.GetUsersAsync();

            Assert.Equal((created.Id, "user@example.com"), (users[^1].Id, users[^1].Email));
            Assert.Empty(users[^1].Roles);
        }

        [Fact]
        public async Task SetUserRolesAsync_LastAdminDemoted_ThrowsConflict()
        {
            UserInfo admin = await Client.GetCurrentUserAsync();

            ByteShelfException exception = await RefusalOf(Client.SetUserRolesAsync(admin.Id, Array.Empty<string>()));

            Assert.Equal(ErrorCodes.Conflict, exception.Code);
        }

        [Fact]
        public async Task DeleteUserAsync_UserWithAGrant_RemovesThemAndTheGrant()
        {
            UserInfo user = await CreateUserAsync("user@example.com");
            await Shelf.SetGrantAsync(user.Id, ShelfAccessPermission.Read);

            await Client.DeleteUserAsync(user.Id);

            Assert.Empty(await Shelf.GetGrantsAsync());
            Assert.DoesNotContain(user.Id, (await Client.GetUsersAsync()).Select(listed => listed.Id));
        }

        [Fact]
        public async Task ChangePasswordAsync_WrongCurrentPassword_ThrowsInvalidCredentials()
        {
            ByteShelfException exception = await RefusalOf(Client.ChangePasswordAsync("not-the-password", "a-new-password"));

            Assert.Equal(ErrorCodes.InvalidCredentials, exception.Code);
        }

        [Fact]
        public async Task ChangePasswordAsync_RightCurrentPassword_ReplacesIt()
        {
            await Client.ChangePasswordAsync(AdminPassword, "a-new-password");

            ByteShelfException exception = await RefusalOf(Client.ChangePasswordAsync(AdminPassword, "another-password"));
            Assert.Equal(ErrorCodes.InvalidCredentials, exception.Code);
            await Client.ChangePasswordAsync("a-new-password", AdminPassword);
        }

        [Fact]
        public async Task SetGrantAsync_ThenGetGrants_ListsTheUser()
        {
            UserInfo user = await CreateUserAsync("user@example.com");

            await Shelf.SetGrantAsync(user.Id, ShelfAccessPermission.ReadWrite);

            ShelfAccessGrant grant = Assert.Single(await Shelf.GetGrantsAsync());
            Assert.Equal((user.Id, "user@example.com", ShelfAccessPermission.ReadWrite), (grant.UserId, grant.Email, grant.Permission));
        }

        [Fact]
        public async Task SetGrantAsync_UnknownUser_ThrowsNotFound()
        {
            ByteShelfException exception = await RefusalOf(Shelf.SetGrantAsync(Guid.NewGuid(), ShelfAccessPermission.Read));

            Assert.Equal(ErrorCodes.NotFound, exception.Code);
        }

        [Fact]
        public async Task RemoveGrantAsync_NoGrant_ThrowsNotFound()
        {
            UserInfo user = await CreateUserAsync("user@example.com");

            ByteShelfException exception = await RefusalOf(Shelf.RemoveGrantAsync(user.Id));

            Assert.Equal(ErrorCodes.NotFound, exception.Code);
        }

        [Fact]
        public async Task CreateKeyAsync_ThenGetKeys_ListsItByItsTrimmedName()
        {
            CreatedShelfKey created = await Shelf.CreateKeyAsync("  backup agent ", ShelfKeyPermission.AddOnly);

            ShelfKeyInfo listed = Assert.Single(await Shelf.GetKeysAsync());

            Assert.StartsWith(ShelfKeys.SecretPrefix, created.Secret, StringComparison.Ordinal);
            Assert.Equal((created.Key.Id, "backup agent", ShelfKeyPermission.AddOnly), (listed.Id, listed.Name, listed.Permission));
        }

        [Fact]
        public async Task CreateKeyAsync_NameWithAControlCharacter_ThrowsInvalidRequest()
        {
            ByteShelfException exception = await RefusalOf(Shelf.CreateKeyAsync("tab\there", ShelfKeyPermission.Read));

            Assert.Equal(ErrorCodes.InvalidRequest, exception.Code);
        }

        [Fact]
        public async Task DeleteKeyAsync_ThenAgain_ThrowsNotFound()
        {
            CreatedShelfKey created = await Shelf.CreateKeyAsync("app", ShelfKeyPermission.Read);
            await Shelf.DeleteKeyAsync(created.Key.Id);

            ByteShelfException exception = await RefusalOf(Shelf.DeleteKeyAsync(created.Key.Id));

            Assert.Equal(ErrorCodes.NotFound, exception.Code);
            Assert.Empty(await Shelf.GetKeysAsync());
        }

        [Theory]
        [InlineData(".")]
        [InlineData("..")]
        [InlineData("Docs")]
        [InlineData("")]
        public void Shelf_NotAShelfName_ThrowsArgumentException(string name)
        {
            // "." and ".." would otherwise be resolved away in the URL, reaching another route.
            Assert.Throws<ArgumentException>(() => Client.Shelf(name));
        }

        [Fact]
        public async Task Calls_TokenAlreadyCancelled_AnswerACancelledTaskAndChangeNothing()
        {
            using CancellationTokenSource cancelled = new CancellationTokenSource();
            await cancelled.CancelAsync();

            Task<ShelfDirectory> created = Shelf.CreateDirectoryAsync("/a", cancelled.Token);
            Task<IReadOnlyList<ShelfInfo>> shelves = Client.GetShelvesAsync(cancelled.Token);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => created);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => shelves);
            Assert.Empty((await Shelf.ListAsync()).Directories);
        }

        /// <summary>A client acting as an admin whose password is <see cref="AdminPassword"/>.</summary>
        protected abstract Task<IByteShelfClient> CreateAdminClientAsync();

        /// <summary>
        /// The refusal <paramref name="call"/> ends in. It takes the task the call answered, so a client
        /// that threw from the call itself, rather than through its task, fails the test there.
        /// </summary>
        protected static Task<ByteShelfException> RefusalOf(Task call)
        {
            return Assert.ThrowsAsync<ByteShelfException>(() => call);
        }

        protected Task<ShelfFile> UploadTextAsync(string path, string text, bool overwrite = false, string? contentType = null)
        {
            return Shelf.UploadAsync(path, new MemoryStream(Encoding.UTF8.GetBytes(text)), new UploadOptions { Overwrite = overwrite, ContentType = contentType });
        }

        protected async Task<string> ReadTextAsync(EntryReference file)
        {
            await using Stream content = await Shelf.OpenReadAsync(file);
            using StreamReader reader = new StreamReader(content);
            return await reader.ReadToEndAsync();
        }

        private Task<UserInfo> CreateUserAsync(string email)
        {
            return Client.CreateUserAsync(new CreateUserRequest { Email = email, DisplayName = "User", Password = "password-for-tests" });
        }

        private static string Sha256Of(string text)
        {
            return TestHashes.Sha256Of(text);
        }

        /// <summary>A stream that can only be read forwards, like a pipe or a network body.</summary>
        private sealed class UnseekableStream : MemoryStream
        {
            public UnseekableStream(byte[] content)
                : base(content)
            {
            }

            public override bool CanSeek => false;
        }
    }
}
