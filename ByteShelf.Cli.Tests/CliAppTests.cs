using System.Net;
using System.Text;
using System.Text.Json;
using ByteShelf.Cli.Commands;
using ByteShelf.Cli.Config;
using ByteShelf.Cli.Connection;
using ByteShelf.Cli.Tests.TestUtilities;
using ByteShelfClient;
using ByteShelfCommon;
using ByteShelfCommon.Serialization;

namespace ByteShelf.Cli.Tests
{
    /// <summary>The commands over an in-memory client: what they send, print and answer.</summary>
    public sealed class CliAppTests : IAsyncLifetime, IDisposable
    {
        private const string AdaPassword = "a-long-password";

        private readonly InMemoryClientSource _clients = new InMemoryClientSource();
        private readonly WorkFolder _work = new WorkFolder();

        private IShelfClient Docs => _clients.Client.Shelf("docs");

        public async Task InitializeAsync()
        {
            await _clients.Client.CreateShelfAsync("docs");
        }

        public Task DisposeAsync()
        {
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _work.Dispose();
        }

        [Fact]
        public async Task Put_ToAPathEndingInASlash_KeepsTheFileNameAndTypesItByExtension()
        {
            await File.WriteAllTextAsync(_work.File("receipt.pdf"), "%PDF");

            CliRun run = await RunAsync("put", _work.File("receipt.pdf"), "docs:/2026/");

            Assert.Equal(CliApp.Succeeded, run.ExitCode);
            ShelfFile stored = await Docs.GetFileAsync("/2026/receipt.pdf");
            Assert.Equal(("application/pdf", 4L), (stored.ContentType, stored.SizeBytes));
        }

        [Fact]
        public async Task Put_WithAType_StoresThatType()
        {
            await File.WriteAllTextAsync(_work.File("data.bin"), "{}");

            await RunAsync("put", _work.File("data.bin"), "docs:/data.bin", "--type", "application/json");

            Assert.Equal("application/json", (await Docs.GetFileAsync("/data.bin")).ContentType);
        }

        [Fact]
        public async Task Put_OverwritingATakenPath_ReplacesTheFile()
        {
            await UploadAsync("/a.txt", "old");
            await File.WriteAllTextAsync(_work.File("a.txt"), "new");

            CliRun refused = await RunAsync("put", _work.File("a.txt"), "docs:/a.txt");
            CliRun replaced = await RunAsync("put", _work.File("a.txt"), "docs:/a.txt", "--overwrite");

            Assert.Equal((CliApp.Failed, CliApp.Succeeded), (refused.ExitCode, replaced.ExitCode));
            Assert.Equal("new", await ReadAsync("/a.txt"));
        }

        [Fact]
        public async Task Put_LocalFileMissing_FailsAsMisuse()
        {
            CliRun run = await RunAsync("put", _work.File("missing.txt"), "docs:/");

            Assert.Equal(CliApp.Misused, run.ExitCode);
            Assert.StartsWith("There is no file", run.Error, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Put_FromStandardInput_StoresWhatWasPiped()
        {
            CliRun run = await RunAsync(new[] { "put", "-", "docs:/notes.txt" }, input: "piped text");

            Assert.Equal(CliApp.Succeeded, run.ExitCode);
            Assert.Equal("piped text", await ReadAsync("/notes.txt"));
        }

        [Fact]
        public async Task Put_FromStandardInputToADirectory_IsRefused()
        {
            CliRun run = await RunAsync(new[] { "put", "-", "docs:/notes/" }, input: "text");

            Assert.Equal(CliApp.Misused, run.ExitCode);
            Assert.Empty((await Docs.ListAsync()).Files);
        }

        [Fact]
        public async Task Get_ToAFileThatExists_IsRefusedAndKeepsIt()
        {
            await UploadAsync("/a.txt", "from the shelf");
            await File.WriteAllTextAsync(_work.File("a.txt"), "local");

            CliRun run = await RunAsync("get", "docs:/a.txt", _work.File("a.txt"));

            Assert.Equal(CliApp.Misused, run.ExitCode);
            Assert.Equal("local", await File.ReadAllTextAsync(_work.File("a.txt")));
        }

        [Fact]
        public async Task Get_Overwriting_ReplacesTheLocalFileAndLeavesNothingElse()
        {
            await UploadAsync("/a.txt", "from the shelf");
            await File.WriteAllTextAsync(_work.File("a.txt"), "local");

            CliRun run = await RunAsync("get", "docs:/a.txt", _work.File("a.txt"), "--overwrite");

            Assert.Equal(CliApp.Succeeded, run.ExitCode);
            Assert.Equal("from the shelf", await File.ReadAllTextAsync(_work.File("a.txt")));
            Assert.Equal(new[] { "a.txt" }, Directory.GetFiles(_work.Path).Select(Path.GetFileName));
        }

        [Fact]
        public async Task Get_IntoALocalDirectory_SavesUnderTheFileName()
        {
            await UploadAsync("/reports/q3.txt", "q3");

            CliRun run = await RunAsync("get", "docs:/reports/q3.txt", _work.Path);

            Assert.Equal(CliApp.Succeeded, run.ExitCode);
            Assert.Equal("q3", await File.ReadAllTextAsync(_work.File("q3.txt")));
        }

        [Fact]
        public async Task Get_ToStandardOutput_WritesOnlyTheBytes()
        {
            await UploadAsync("/a.txt", "the bytes");

            CliRun run = await RunAsync("get", "docs:/a.txt", "-");

            Assert.Equal(("the bytes", string.Empty), (Encoding.UTF8.GetString(run.BinaryOutput), run.Output));
        }

        [Fact]
        public async Task Get_TheShelfsRoot_FailsAsMisuse()
        {
            CliRun run = await RunAsync("get", "docs");

            Assert.Equal(CliApp.Misused, run.ExitCode);
            Assert.Contains("name a file", run.Error, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Get_DownloadDoesNotMatchItsHash_FailsAndLeavesNoFile()
        {
            using ScriptedHandler server = new ScriptedHandler(_ =>
            {
                HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("tampered") };
                response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue($"\"{new string('0', 64)}\"");
                return response;
            });
            CliConfigStore store = new CliConfigStore(_work.File("config/config.json"));
            store.Save(new CliConfig { Server = "https://files.test/", Login = new KeyLogin { Key = "bsk_key" } });
            using ConfiguredClientSource clients = new ConfiguredClientSource(store, _ => null, server);

            CliRun run = await CliRun.RunAsync(clients, new[] { "get", "docs:/a.txt", _work.File("a.txt") });

            Assert.Equal(CliApp.Failed, run.ExitCode);
            Assert.Contains("SHA-256", run.Error, StringComparison.Ordinal);
            Assert.Empty(Directory.GetFiles(_work.Path));
        }

        [Fact]
        public async Task Stat_AFileOrADirectory_PrintsItsRecord()
        {
            ShelfFile file = await UploadAsync("/a/b.txt", "b");

            CliRun ofFile = await RunAsync("stat", "docs:/a/b.txt", "--json");
            CliRun ofDirectory = await RunAsync("stat", "docs:/a", "--json");

            Assert.Equal(file.Id, JsonSerializer.Deserialize(ofFile.Output, ByteShelfJsonContext.Default.ShelfFile)!.Id);
            Assert.Equal("/a", JsonSerializer.Deserialize(ofDirectory.Output, ByteShelfJsonContext.Default.ShelfDirectory)!.Path);
        }

        [Fact]
        public async Task Mkdir_CreatesItWithItsParents()
        {
            CliRun run = await RunAsync("mkdir", "docs:/a/b");

            Assert.Equal(CliApp.Succeeded, run.ExitCode);
            Assert.Equal("b", Assert.Single((await Docs.ListAsync("/a")).Directories).Name);
        }

        [Fact]
        public async Task Mv_AFile_MovesTheFile()
        {
            await UploadAsync("/a.txt", "a");

            CliRun run = await RunAsync("mv", "docs:/a.txt", "/archive/b.txt");

            Assert.Equal((CliApp.Succeeded, "Moved to /archive/b.txt"), (run.ExitCode, run.Output));
            Assert.Equal("a", await ReadAsync("/archive/b.txt"));
        }

        [Fact]
        public async Task Mv_ADirectory_MovesEverythingInIt()
        {
            await UploadAsync("/photos/a.jpg", "a");

            CliRun run = await RunAsync("mv", "docs:/photos", "/archive/photos");

            Assert.Equal(CliApp.Succeeded, run.ExitCode);
            Assert.Equal("a", await ReadAsync("/archive/photos/a.jpg"));
        }

        [Fact]
        public async Task Rm_ThenRestoreWithTheDeletionId_PutsItBack()
        {
            await UploadAsync("/a/b.txt", "b");

            CliRun removed = await RunAsync("rm", "docs:/a", "--json");
            TrashEntry entry = JsonSerializer.Deserialize(removed.Output, ByteShelfJsonContext.Default.TrashEntry)!;
            CliRun restored = await RunAsync("restore", "docs", entry.DeletionId.ToString());

            Assert.Equal((TrashEntryKind.Directory, CliApp.Succeeded), (entry.Kind, restored.ExitCode));
            Assert.Equal("b", await ReadAsync("/a/b.txt"));
        }

        [Fact]
        public async Task Rm_NothingThere_FailsWithNotFound()
        {
            CliRun run = await RunAsync("rm", "docs:/missing");

            Assert.Equal(CliApp.Failed, run.ExitCode);
            Assert.Contains("Neither a file nor a directory", run.Error, StringComparison.Ordinal);
            Assert.EndsWith($"({ErrorCodes.NotFound})", run.Error, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Trash_ThenEmptyTrash_ListsThenForgetsTheDeletion()
        {
            await UploadAsync("/a.txt", "a");
            TrashEntry deleted = await Docs.DeleteFileAsync("/a.txt");

            CliRun listed = await RunAsync("trash", "docs");
            CliRun emptied = await RunAsync("empty-trash", "docs");

            Assert.StartsWith(deleted.DeletionId.ToString("D"), listed.Output, StringComparison.Ordinal);
            Assert.Equal((CliApp.Succeeded, "Emptied the trash."), (emptied.ExitCode, emptied.Output));
            Assert.Empty(await Docs.GetTrashAsync());
        }

        [Fact]
        public async Task Ls_WithJson_PrintsTheListing()
        {
            await UploadAsync("/a.txt", "a");

            CliRun run = await RunAsync("ls", "docs", "--json");

            DirectoryListing listing = JsonSerializer.Deserialize(run.Output, ByteShelfJsonContext.Default.DirectoryListing)!;
            Assert.Equal("a.txt", Assert.Single(listing.Files).Name);
        }

        [Fact]
        public async Task Json_CommandWithoutAResult_PrintsNothing()
        {
            CliRun run = await RunAsync("empty-trash", "docs", "--json");

            Assert.Equal((CliApp.Succeeded, string.Empty), (run.ExitCode, run.Output));
        }

        [Fact]
        public async Task Shelves_ListsEveryShelfWithItsUsage()
        {
            await _clients.Client.CreateShelfAsync("photos");

            CliRun run = await RunAsync("shelves");

            Assert.Equal(new[] { "docs  0 B used  0 files", "photos  0 B used  0 files" }, run.Output.Split(Environment.NewLine));
        }

        [Fact]
        public async Task ShelfCreate_WithAQuota_ThenInfoShowsIt()
        {
            await RunAsync("shelf", "create", "photos", "--quota", "2048");

            CliRun run = await RunAsync("shelf", "info", "photos");

            Assert.Equal((CliApp.Succeeded, "photos  0 B of 2 KiB used  0 files"), (run.ExitCode, run.Output));
        }

        [Fact]
        public async Task ShelfUpdate_OnlyANewName_KeepsTheQuota()
        {
            await Docs.UpdateAsync("docs", 1000);

            CliRun run = await RunAsync("shelf", "update", "docs", "--name", "papers");

            Assert.Equal(CliApp.Succeeded, run.ExitCode);
            Assert.Equal(1000, (await _clients.Client.Shelf("papers").GetInfoAsync()).QuotaBytes);
        }

        [Fact]
        public async Task ShelfUpdate_NoQuota_RemovesIt()
        {
            await Docs.UpdateAsync("docs", 1000);

            CliRun run = await RunAsync("shelf", "update", "docs", "--no-quota");

            Assert.Equal(CliApp.Succeeded, run.ExitCode);
            Assert.Null((await Docs.GetInfoAsync()).QuotaBytes);
        }

        [Fact]
        public async Task ShelfDelete_EmptyShelf_RemovesIt()
        {
            CliRun run = await RunAsync("shelf", "delete", "docs");

            Assert.Equal(CliApp.Succeeded, run.ExitCode);
            Assert.Empty(await _clients.Client.GetShelvesAsync());
        }

        [Fact]
        public async Task WhoAmI_AsAUser_SaysWhoAndWhere()
        {
            CliRun run = await RunAsync("whoami");

            Assert.Equal(CliApp.Succeeded, run.ExitCode);
            Assert.StartsWith("memory as admin", run.Output, StringComparison.Ordinal);
            Assert.Contains(_clients.Client.CurrentUser.Email, run.Output, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Passwd_ReadsBothPasswordsFromStandardInput()
        {
            CliRun run = await RunAsync(new[] { "passwd" }, input: $"password-for-tests{Environment.NewLine}a-new-password{Environment.NewLine}");

            Assert.Equal(CliApp.Succeeded, run.ExitCode);
            await _clients.Client.ChangePasswordAsync("a-new-password", "password-for-tests");
        }

        [Fact]
        public async Task Passwd_NothingOnStandardInput_FailsAsMisuse()
        {
            CliRun run = await RunAsync("passwd");

            Assert.Equal((CliApp.Misused, "Nothing was given."), (run.ExitCode, run.Error));
        }

        [Fact]
        public async Task UserCreate_ThenGrantByEmail_GivesThemAccess()
        {
            CliRun created = await CreateAdaAsync();
            CliRun granted = await RunAsync("grant", "docs", "ADA@example.com", "read-write");

            Assert.Equal((CliApp.Succeeded, CliApp.Succeeded), (created.ExitCode, granted.ExitCode));
            ShelfAccessGrant grant = Assert.Single(await Docs.GetGrantsAsync());
            Assert.Equal(("ada@example.com", ShelfAccessPermission.ReadWrite), (grant.Email, grant.Permission));
        }

        [Fact]
        public async Task Revoke_TakesTheAccessAway()
        {
            await CreateAdaAsync();
            await RunAsync("grant", "docs", "ada@example.com", "read");

            CliRun run = await RunAsync("revoke", "docs", "ada@example.com");

            Assert.Equal(CliApp.Succeeded, run.ExitCode);
            Assert.Empty(await Docs.GetGrantsAsync());
        }

        [Fact]
        public async Task UserAdmin_ThenNoAdmin_ChangesTheRole()
        {
            await CreateAdaAsync();

            CliRun madeAdmin = await RunAsync("user", "admin", "ada@example.com");
            IReadOnlyList<string> rolesAsAdmin = (await FindUserAsync("ada@example.com")).Roles;
            CliRun madeUser = await RunAsync("user", "no-admin", "ada@example.com");

            Assert.Equal((CliApp.Succeeded, CliApp.Succeeded), (madeAdmin.ExitCode, madeUser.ExitCode));
            Assert.Contains(UserRoles.Admin, rolesAsAdmin);
            Assert.Empty((await FindUserAsync("ada@example.com")).Roles);
        }

        [Fact]
        public async Task UserDelete_ByEmail_RemovesThem()
        {
            await CreateAdaAsync();

            CliRun run = await RunAsync("user", "delete", "ada@example.com");

            Assert.Equal(CliApp.Succeeded, run.ExitCode);
            Assert.DoesNotContain(await _clients.Client.GetUsersAsync(), user => user.Email == "ada@example.com");
        }

        [Fact]
        public async Task KeyCreate_ShowsTheSecretThenNeverAgain()
        {
            CliRun created = await RunAsync("key", "create", "docs", "backup job", "add-only");
            CliRun listed = await RunAsync("keys", "docs");

            string secret = created.Output.Split(Environment.NewLine)[^1];
            Assert.StartsWith(ShelfKeys.SecretPrefix, secret, StringComparison.Ordinal);
            Assert.Contains("backup job  add-only", listed.Output, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, listed.Output, StringComparison.Ordinal);
        }

        [Fact]
        public async Task KeyDelete_RemovesTheKey()
        {
            CreatedShelfKey key = await Docs.CreateKeyAsync("job", ShelfKeyPermission.Read);

            CliRun run = await RunAsync("key", "delete", "docs", key.Key.Id.ToString());

            Assert.Equal(CliApp.Succeeded, run.ExitCode);
            Assert.Empty(await Docs.GetKeysAsync());
        }

        [Fact]
        public async Task ServerRefusal_FailsWithItsCode()
        {
            CliRun run = await RunAsync("shelf", "create", "docs");

            Assert.Equal(CliApp.Failed, run.ExitCode);
            Assert.EndsWith($"({ErrorCodes.Conflict})", run.Error, StringComparison.Ordinal);
        }

        [Fact]
        public async Task ServerUnreachable_FailsSayingSo()
        {
            using ScriptedHandler unreachable = new ScriptedHandler(_ => throw new HttpRequestException("No route to host."));
            CliConfigStore store = new CliConfigStore(_work.File("config/config.json"));
            store.Save(new CliConfig { Server = "https://files.test/", Login = new KeyLogin { Key = "bsk_key" } });
            using ConfiguredClientSource clients = new ConfiguredClientSource(store, _ => null, unreachable);

            CliRun run = await CliRun.RunAsync(clients, new[] { "shelves" });

            Assert.Equal((CliApp.Failed, "Could not reach the server: No route to host."), (run.ExitCode, run.Error));
        }

        [Fact]
        public async Task Interrupted_AnswersTheInterruptCode()
        {
            using CancellationTokenSource interrupted = new CancellationTokenSource();
            await interrupted.CancelAsync();

            CliRun run = await CliRun.RunAsync(_clients, new[] { "put", "-", "docs:/a.txt" }, "text", interrupted.Token);

            Assert.Equal(CliApp.Interrupted, run.ExitCode);
        }

        [Theory]
        [InlineData("Missing <shelf:path>.", "ls")]
        [InlineData("Unexpected 'extra'.", "ls", "docs", "extra")]
        [InlineData("Unknown option --recursive.", "ls", "docs", "--recursive")]
        [InlineData("'shelf' needs a subcommand: shelf info, shelf create, shelf update, shelf delete.", "shelf")]
        [InlineData("Unknown command 'frobnicate'. Run 'byteshelf help' for the commands.", "frobnicate")]
        [InlineData("'Docs' is not a shelf name", "ls", "Docs:/")]
        [InlineData("is not a media type", "put", "-", "docs:/a.txt", "--type", "not a type")]
        [InlineData("Give --quota or --no-quota, not both.", "shelf", "update", "docs", "--quota", "5", "--no-quota")]
        [InlineData("Sign in with --email <email> or with --key, one of them.", "login", "https://files.test", "--email", "a@b.test", "--key")]
        [InlineData("Sign in with --email <email> or with --key, one of them.", "login", "https://files.test")]
        [InlineData("'nope' is not a deletion id", "restore", "docs", "nope")]
        [InlineData("'nope' is not a key id", "key", "delete", "docs", "nope")]
        [InlineData("'write' is not an access; give read|read-write.", "grant", "docs", "a@b.test", "write")]
        public async Task Misuse_FailsWithTheUsageCodeAndSaysWhy(string message, params string[] commandLine)
        {
            CliRun run = await RunAsync(commandLine);

            Assert.Equal(CliApp.Misused, run.ExitCode);
            Assert.Contains(message, run.Error, StringComparison.Ordinal);
        }

        [Fact]
        public void Help_ListsEveryCommandOnce()
        {
            string help = new CliApp(_clients, new Terminal(TextReader.Null, TextWriter.Null, TextWriter.Null, false, () => Stream.Null, () => Stream.Null)).Help;

            Assert.Single(help.Split(Environment.NewLine), line => line.StartsWith("  key create <shelf>", StringComparison.Ordinal));
            Assert.Contains("--mcp", help, StringComparison.Ordinal);
        }

        private Task<CliRun> RunAsync(params string[] commandLine)
        {
            return CliRun.RunAsync(_clients, commandLine);
        }

        private Task<CliRun> RunAsync(string[] commandLine, string input)
        {
            return CliRun.RunAsync(_clients, commandLine, input);
        }

        private Task<CliRun> CreateAdaAsync()
        {
            return RunAsync(new[] { "user", "create", "ada@example.com", "--name", "Ada" }, input: AdaPassword + Environment.NewLine);
        }

        private Task<ShelfFile> UploadAsync(string path, string text)
        {
            return Docs.UploadAsync(path, new MemoryStream(Encoding.UTF8.GetBytes(text)));
        }

        private async Task<string> ReadAsync(string path)
        {
            await using Stream content = await Docs.OpenReadAsync(path);
            using StreamReader reader = new StreamReader(content);
            return await reader.ReadToEndAsync();
        }

        private async Task<UserInfo> FindUserAsync(string email)
        {
            return (await _clients.Client.GetUsersAsync()).Single(user => user.Email == email);
        }
    }
}
