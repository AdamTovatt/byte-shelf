using System.Text;
using ByteShelf.Cli.Commands;
using ByteShelf.Cli.Tests.TestUtilities;
using ByteShelf.Tools;
using ByteShelfClient;
using ByteShelfClient.Encryption;
using ByteShelfCommon;

namespace ByteShelf.Cli.Tests
{
    /// <summary>encrypt, decrypt, and put and get of encrypted files, over an in-memory client.</summary>
    public sealed class CliEncryptionTests : IAsyncLifetime, IDisposable
    {
        private const string Password = "räksmörgås at noon";

        // What these tests encrypt with: age's default takes seconds a file in a test build.
        private const int FastWorkFactor = 10;

        private readonly InMemoryClientSource _clients = new InMemoryClientSource();
        private readonly WorkFolder _work = new WorkFolder();

        private IShelfClient Docs => _clients.Client.Shelf("docs");

        private string PasswordFile => _work.File("password.txt");

        public async Task InitializeAsync()
        {
            await _clients.Client.CreateShelfAsync("docs");
            await File.WriteAllTextAsync(PasswordFile, Password + "\n");
        }

        public Task DisposeAsync()
        {
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _work.Dispose();
        }

        /// <summary>Slow on purpose (a real work factor 18 scrypt): the CLI as it runs must never encrypt below age's default.</summary>
        [Fact]
        public async Task PutEncrypt_TheCliAsItRuns_EncryptsAtAgesDefaultWorkFactor()
        {
            await File.WriteAllTextAsync(_work.File("a.txt"), "a");

            CliRun run = await CliRun.RunAsync(_clients, new[] { "put", _work.File("a.txt"), "docs:/", "--encrypt", "--password-file", PasswordFile });

            Assert.Equal(CliApp.Succeeded, run.ExitCode);
            Assert.EndsWith($" {AgeEncryption.DefaultWorkFactor}", (await ReadAsync("/a.txt")).Split('\n')[1], StringComparison.Ordinal);
        }

        [Fact]
        public async Task EncryptAFolder_ThenGetAndDecrypt_RoundTripsItsFiles()
        {
            await UploadAsync("/secret/a.txt", "first");
            await UploadAsync("/secret/inner/b.txt", "second");

            CliRun encrypted = await RunAsync("encrypt", "docs:/secret", "--password-file", PasswordFile);
            ShelfFile stored = await Docs.GetFileAsync("/secret/inner/b.txt");
            CliRun saved = await RunAsync("get", "docs:/secret/a.txt", _work.File("a.txt"), "--password-file", PasswordFile);
            CliRun decrypted = await RunAsync("decrypt", "docs:/secret", "--password-file", PasswordFile);

            Assert.Equal((CliApp.Succeeded, CliApp.Succeeded, CliApp.Succeeded), (encrypted.ExitCode, saved.ExitCode, decrypted.ExitCode));
            Assert.StartsWith("Encrypted 2 files", encrypted.Output, StringComparison.Ordinal);
            Assert.Equal((true, "text/plain"), (stored.Encrypted, stored.ContentType));
            Assert.Equal("first", await File.ReadAllTextAsync(_work.File("a.txt")));
            Assert.Equal(("second", false), (await ReadAsync("/secret/inner/b.txt"), (await Docs.GetFileAsync("/secret/inner/b.txt")).Encrypted));
            Assert.Null((await Docs.GetEncryptionAsync("/secret/a.txt")).EncryptedBy);
        }

        [Fact]
        public async Task EncryptAFolder_RunAgainAfterItStopped_EncryptsWhatIsLeftAndKeepsWhatIsDone()
        {
            await UploadAsync("/secret/plain.txt", "plain");
            await Docs.EncryptDirectoryAsync("/secret", await FolderPassword.CreateCheckAsync(Password, CancellationToken.None, FastWorkFactor));
            ShelfFile done = await StoreEncryptedAsync("/secret/done.txt", "done", Password);

            CliRun run = await RunAsync("encrypt", "docs:/secret", "--password-file", PasswordFile);

            Assert.Equal(CliApp.Succeeded, run.ExitCode);
            Assert.True((await Docs.GetFileAsync("/secret/plain.txt")).Encrypted);
            Assert.Equal(done.Id, (await Docs.GetFileAsync("/secret/done.txt")).Id);
        }

        [Fact]
        public async Task DecryptAFolder_RunAgainAfterItStopped_DecryptsWhatIsStillEncrypted()
        {
            await UploadAsync("/secret/a.txt", "first");
            await RunAsync("encrypt", "docs:/secret", "--password-file", PasswordFile);
            await Docs.RemoveDirectoryEncryptionAsync("/secret");

            CliRun run = await RunAsync("decrypt", "docs:/secret", "--password-file", PasswordFile);

            Assert.Equal(CliApp.Succeeded, run.ExitCode);
            Assert.Equal(("first", false), (await ReadAsync("/secret/a.txt"), (await Docs.GetFileAsync("/secret/a.txt")).Encrypted));
        }

        [Fact]
        public async Task DecryptAFolder_HoldingAnEncryptedFolder_FailsAsMisuseNamingIt()
        {
            await Docs.CreateDirectoryAsync("/outer/secret");
            await RunAsync("encrypt", "docs:/outer/secret", "--password-file", PasswordFile);

            CliRun run = await RunAsync("decrypt", "docs:/outer", "--password-file", PasswordFile);

            Assert.Equal(CliApp.Misused, run.ExitCode);
            Assert.Contains("'/outer/secret' is under '/outer'; decrypt that folder itself", run.Error, StringComparison.Ordinal);
        }

        [Fact]
        public async Task EncryptAFolder_HoldingAFileEncryptedWithAnotherPassword_FailsAsMisuseAndEncryptsNothing()
        {
            await StoreEncryptedAsync("/secret/theirs.txt", "theirs", "another password");
            await UploadAsync("/secret/plain.txt", "plain");

            CliRun run = await RunAsync("encrypt", "docs:/secret", "--password-file", PasswordFile);

            Assert.Equal(CliApp.Misused, run.ExitCode);
            Assert.Contains("Nothing was encrypted: '/secret/theirs.txt' is encrypted with another password", run.Error, StringComparison.Ordinal);
            Assert.Null((await Docs.GetEncryptionAsync("/secret/plain.txt")).EncryptedBy);
            Assert.False((await Docs.GetFileAsync("/secret/plain.txt")).Encrypted);
        }

        [Fact]
        public async Task EncryptAFolder_InsideAnEncryptedOne_FailsAsMisuse()
        {
            await Docs.CreateDirectoryAsync("/secret/inner");
            await RunAsync("encrypt", "docs:/secret", "--password-file", PasswordFile);

            CliRun run = await RunAsync("encrypt", "docs:/secret/inner", "--password-file", PasswordFile);

            Assert.Equal(CliApp.Misused, run.ExitCode);
            Assert.Contains("inside the encrypted folder '/secret'", run.Error, StringComparison.Ordinal);
        }

        [Fact]
        public async Task EncryptThenDecrypt_OneFile_RoundTripsIt()
        {
            await UploadAsync("/a.txt", "a");

            CliRun encrypted = await RunAsync("encrypt", "docs:/a.txt", "--password-file", PasswordFile);
            bool wasEncrypted = (await Docs.GetFileAsync("/a.txt")).Encrypted;
            CliRun decrypted = await RunAsync("decrypt", "docs:/a.txt", "--password-file", PasswordFile);

            Assert.Equal((CliApp.Succeeded, true, CliApp.Succeeded), (encrypted.ExitCode, wasEncrypted, decrypted.ExitCode));
            Assert.Equal(("a", false), (await ReadAsync("/a.txt"), (await Docs.GetFileAsync("/a.txt")).Encrypted));
        }

        [Fact]
        public async Task Decrypt_OneFileUnderAnEncryptedFolder_FailsAsMisuseAndChangesNothing()
        {
            await UploadAsync("/secret/a.txt", "a");
            await RunAsync("encrypt", "docs:/secret", "--password-file", PasswordFile);

            CliRun run = await RunAsync("decrypt", "docs:/secret/a.txt", "--password-file", PasswordFile);

            Assert.Equal(CliApp.Misused, run.ExitCode);
            Assert.Contains("remove that folder's encryption instead", run.Error, StringComparison.Ordinal);
            Assert.True((await Docs.GetFileAsync("/secret/a.txt")).Encrypted);
        }

        [Fact]
        public async Task PutEncrypt_ThenGetToStdoutWithThePassword_WritesTheContents()
        {
            await File.WriteAllTextAsync(_work.File("a.txt"), "contents");

            CliRun put = await RunAsync("put", _work.File("a.txt"), "docs:/", "--encrypt", "--password-file", PasswordFile);
            CliRun get = await RunAsync("get", "docs:/a.txt", "-", "--password-file", PasswordFile);

            Assert.Equal((CliApp.Succeeded, CliApp.Succeeded), (put.ExitCode, get.ExitCode));
            Assert.True((await Docs.GetFileAsync("/a.txt")).Encrypted);
            Assert.Equal("contents", Encoding.UTF8.GetString(get.BinaryOutput));
        }

        [Fact]
        public async Task Get_AnEncryptedFileWithoutAPassword_SavesItsAgeFileAndSaysSo()
        {
            await EncryptSecretAsync();
            await File.WriteAllTextAsync(_work.File("plain.txt"), "plain");
            await RunAsync("put", _work.File("plain.txt"), "docs:/secret/", "--password-file", PasswordFile);

            CliRun run = await RunAsync("get", "docs:/secret/plain.txt", _work.Path);

            Assert.Equal(CliApp.Succeeded, run.ExitCode);
            Assert.Contains("encrypted as it is stored", run.Output, StringComparison.Ordinal);
            Assert.StartsWith("age-encryption.org/v1\n", await File.ReadAllTextAsync(_work.File("plain.txt.age")), StringComparison.Ordinal);
        }

        [Fact]
        public async Task Get_OneFileWithAnotherPassword_FailsAsMisuseSayingSoAndSavesNothing()
        {
            await StoreEncryptedAsync("/a.txt", "a", Password);
            await File.WriteAllTextAsync(_work.File("wrong.txt"), "another password\n");

            CliRun run = await RunAsync("get", "docs:/a.txt", _work.File("a.txt"), "--password-file", _work.File("wrong.txt"));

            Assert.Equal(CliApp.Misused, run.ExitCode);
            Assert.Contains("The password is wrong.", run.Error, StringComparison.Ordinal);
            Assert.False(File.Exists(_work.File("a.txt")));
        }

        [Fact]
        public async Task GetAFolder_WithItsPassword_SavesItsFilesDecrypted()
        {
            await UploadAsync("/secret/a.txt", "a");
            await RunAsync("encrypt", "docs:/secret", "--password-file", PasswordFile);

            CliRun run = await RunAsync("get", "docs:/secret", _work.File("out"), "--decrypt", "--password-file", PasswordFile);

            Assert.Equal(CliApp.Succeeded, run.ExitCode);
            Assert.Equal("a", await File.ReadAllTextAsync(Path.Combine(_work.File("out"), "a.txt")));
        }

        [Fact]
        public async Task GetAFolder_WithAnotherPassword_FailsAsMisuseBeforeSavingAnything()
        {
            await UploadAsync("/secret/a.txt", "a");
            await RunAsync("encrypt", "docs:/secret", "--password-file", PasswordFile);
            await File.WriteAllTextAsync(_work.File("wrong.txt"), "another password\n");

            CliRun run = await RunAsync("get", "docs:/secret", _work.File("out"), "--password-file", _work.File("wrong.txt"));

            Assert.Equal(CliApp.Misused, run.ExitCode);
            Assert.Contains("not the password of the encrypted folder '/secret'", run.Error, StringComparison.Ordinal);
            Assert.False(Directory.Exists(_work.File("out")));
        }

        [Fact]
        public async Task Put_IntoAnEncryptedFolderWithAnotherPassword_FailsAsMisuseAndStoresNothing()
        {
            await EncryptSecretAsync();
            await File.WriteAllTextAsync(_work.File("wrong.txt"), "another password\n");
            await File.WriteAllTextAsync(_work.File("a.txt"), "a");

            CliRun run = await RunAsync("put", _work.File("a.txt"), "docs:/secret/", "--password-file", _work.File("wrong.txt"));

            Assert.Equal(CliApp.Misused, run.ExitCode);
            Assert.Contains("not the password of the encrypted folder '/secret'", run.Error, StringComparison.Ordinal);
            Assert.Empty((await Docs.ListAsync("/secret")).Files);
        }

        [Fact]
        public async Task Put_FromStdinIntoAnEncryptedFolderWithoutAPasswordFile_FailsAsMisuse()
        {
            await EncryptSecretAsync();

            CliRun run = await CliRun.RunAsync(_clients, new[] { "put", "-", "docs:/secret/a.txt" }, "contents", encryptionWorkFactor: FastWorkFactor);

            Assert.Equal(CliApp.Misused, run.ExitCode);
            Assert.Contains("--password-file", run.Error, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Encrypt_NewPasswordTypedTwiceDifferently_FailsAsMisuseAndEncryptsNothing()
        {
            await UploadAsync("/a.txt", "a");

            CliRun run = await CliRun.RunAsync(_clients, new[] { "encrypt", "docs:/a.txt" }, "one\ntwo\n", encryptionWorkFactor: FastWorkFactor);

            Assert.Equal(CliApp.Misused, run.ExitCode);
            Assert.False((await Docs.GetFileAsync("/a.txt")).Encrypted);
        }

        [Fact]
        public async Task TreeFindAndStat_MarkWhatIsEncrypted()
        {
            await UploadAsync("/secret/a.txt", "a");
            await RunAsync("encrypt", "docs:/secret", "--password-file", PasswordFile);

            CliRun tree = await RunAsync("tree", "docs:/");
            CliRun find = await RunAsync("find", "docs:/", "a.txt");
            CliRun statFile = await RunAsync("stat", "docs:/secret/a.txt");
            CliRun statDirectory = await RunAsync("stat", "docs:/secret");

            Assert.Contains("/secret/  (encrypted)", tree.Output, StringComparison.Ordinal);
            Assert.Contains(tree.Output.Split('\n'), line => line.StartsWith("/secret/a.txt", StringComparison.Ordinal) && line.EndsWith("(encrypted)", StringComparison.Ordinal));
            Assert.EndsWith("(encrypted)", find.Output.Split('\n')[0], StringComparison.Ordinal);
            Assert.Contains("encrypted", statFile.Output.Split('\n').Select(line => line.TrimStart().Split(' ')[0]));
            Assert.EndsWith("(encrypted)", statDirectory.Output, StringComparison.Ordinal);
        }

        /// <summary>An empty /secret, encrypted with <see cref="Password"/>.</summary>
        private async Task EncryptSecretAsync()
        {
            await Docs.CreateDirectoryAsync("/secret");
            Assert.Equal(CliApp.Succeeded, (await RunAsync("encrypt", "docs:/secret", "--password-file", PasswordFile)).ExitCode);
        }

        private Task<CliRun> RunAsync(params string[] commandLine)
        {
            return CliRun.RunAsync(_clients, commandLine, encryptionWorkFactor: FastWorkFactor);
        }

        private Task<ShelfFile> UploadAsync(string path, string text)
        {
            return Docs.UploadAsync(path, new MemoryStream(Encoding.UTF8.GetBytes(text)));
        }

        private Task<ShelfFile> StoreEncryptedAsync(string path, string text, string password)
        {
            return Transfers.UploadAsync(Docs, path, new MemoryStream(Encoding.UTF8.GetBytes(text)), new UploadOptions(), new Encryptor(password, FastWorkFactor), CancellationToken.None);
        }

        private async Task<string> ReadAsync(string path)
        {
            await using Stream content = await Docs.OpenReadAsync(path);
            using StreamReader reader = new StreamReader(content);
            return await reader.ReadToEndAsync();
        }
    }
}
