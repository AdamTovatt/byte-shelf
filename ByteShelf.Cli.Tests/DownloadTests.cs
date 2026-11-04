using System.Net;
using System.Text;
using System.Text.Json;
using ByteShelf.Cli.Commands;
using ByteShelf.Cli.Config;
using ByteShelf.Cli.Connection;
using ByteShelf.Cli.Tests.TestUtilities;
using ByteShelf.Tools;
using ByteShelfClient;
using ByteShelfCommon;
using ByteShelfCommon.Serialization;

namespace ByteShelf.Cli.Tests
{
    /// <summary>Where downloads may write, and what they say when they cannot: the cases a plain shelf does not reach.</summary>
    public sealed class DownloadTests : IAsyncLifetime, IDisposable
    {
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
        public async Task DownloadToFile_TwoDownloadsToOnePath_NeitherBreaksTheOther()
        {
            ShelfFile one = await UploadAsync("/one.txt", "first");
            ShelfFile two = await UploadAsync("/two.txt", "second");
            TaskCompletionSource reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            IShelfClient shelf = ShelfStandIn.Over(Docs, (method, args) => method.Name == nameof(IShelfClient.OpenReadAsync) && ((EntryReference)args![0]!).Id == one.Id
                ? Task.FromResult<Stream>(new HeldStream(Encoding.UTF8.GetBytes("first"), reading, release.Task))
                : null);
            string target = _work.File("a.txt");

            // The first holds its side file open, mid-download, while the second runs from start to end.
            Task<string> first = Transfers.DownloadToFileAsync(FileSource.Live(shelf), one, target, overwrite: true, EncryptedFiles.SavedAsTheyAre, CancellationToken.None);
            await reading.Task;
            await Transfers.DownloadToFileAsync(FileSource.Live(shelf), two, target, overwrite: true, EncryptedFiles.SavedAsTheyAre, CancellationToken.None);
            string secondSaved = await File.ReadAllTextAsync(target);
            release.SetResult();
            await first;

            Assert.Equal(("second", "first"), (secondSaved, await File.ReadAllTextAsync(target)));
            Assert.Equal(new[] { "a.txt" }, Directory.GetFiles(_work.Path).Select(Path.GetFileName));
        }

        [Fact]
        public async Task DownloadDirectory_EntryWhosePathLeavesTheFolder_IsLeftOutAndNothingLandsOutside()
        {
            // No shelf holds such a name; a server that answered one must still not reach outside.
            ShelfDirectory directory = await Docs.CreateDirectoryAsync("/r");
            await UploadAsync("/r/kept.txt", "kept");
            ShelfFile kept = await Docs.GetFileAsync("/r/kept.txt");
            ShelfFile escaping = new ShelfFile
            {
                Id = kept.Id,
                ShelfId = kept.ShelfId,
                DirectoryId = kept.DirectoryId,
                Path = "/r/../../escaped.txt",
                Name = "escaped.txt",
                ContentType = kept.ContentType,
                SizeBytes = kept.SizeBytes,
                Sha256 = kept.Sha256,
                CreatedAt = kept.CreatedAt,
                UpdatedAt = kept.UpdatedAt,
                Encrypted = kept.Encrypted,
            };
            IShelfClient shelf = ShelfStandIn.Over(Docs, (method, _) => method.Name == nameof(IShelfClient.ListTreeAsync)
                ? Task.FromResult(new DirectoryTree { Directory = directory, Directories = Array.Empty<ShelfDirectory>(), Files = new[] { escaping, kept } })
                : null);

            FolderTransferResult result = await FolderTransfers.DownloadAsync(shelf, ShelfLocation.Parse("docs:/r"), _work.File("out/r"), overwrite: false, EncryptedFiles.SavedAsTheyAre, CancellationToken.None);

            Assert.Equal("/r/../../escaped.txt: its name is not one a local file can have here.", Assert.Single(result.LeftOut));
            Assert.Equal(1, result.Files);
            Assert.Equal(new[] { _work.File("out/r/kept.txt") }, Directory.GetFiles(_work.Path, "*", SearchOption.AllDirectories));
        }

        [Fact]
        public async Task Get_ADirectoryOverALocalFolderThatIsALink_LeavesOutWhatIsUnderItAndWritesNothingThrough()
        {
            await UploadAsync("/r/sub/a.txt", "a");
            await UploadAsync("/r/b.txt", "b");
            Directory.CreateDirectory(_work.File("elsewhere"));
            Directory.CreateDirectory(_work.File("out/r"));
            Directory.CreateSymbolicLink(_work.File("out/r/sub"), _work.File("elsewhere"));

            CliRun run = await RunAsync("get", "docs:/r", _work.File("out"));

            Assert.Equal(CliApp.Failed, run.ExitCode);
            Assert.Contains($"/r/sub: {_work.File("out/r/sub")} is a symbolic link, which is never followed.", run.Output, StringComparison.Ordinal);
            Assert.Contains("/r/sub/a.txt: it is inside /r/sub, which was left out.", run.Output, StringComparison.Ordinal);
            Assert.Empty(Directory.GetFileSystemEntries(_work.File("elsewhere")));
            Assert.Equal("b", await File.ReadAllTextAsync(_work.File("out/r/b.txt")));
        }

        [Fact]
        public async Task Get_ADirectoryPartlyWithJson_PrintsNothingAndFailsSayingWhatWasLeftOut()
        {
            await UploadAsync("/r/a.txt", "from the shelf");
            await UploadAsync("/r/b.txt", "b");
            Directory.CreateDirectory(_work.File("r"));
            await File.WriteAllTextAsync(_work.File("r/a.txt"), "local");

            CliRun run = await RunAsync("get", "docs:/r", _work.Path, "--json");

            Assert.Equal((CliApp.Failed, string.Empty, "Left out 1 file or folder."), (run.ExitCode, run.Output, run.Error));
            Assert.Equal("b", await File.ReadAllTextAsync(_work.File("r/b.txt")));
        }

        [Fact]
        public async Task Get_ToStandardOutputNotMatchingItsHash_WritesTheBytesThenFails()
        {
            using ScriptedHandler server = new ScriptedHandler(request =>
            {
                // get first asks whether the path is a file or a directory.
                if (request.RequestUri!.AbsolutePath.EndsWith("/files", StringComparison.Ordinal))
                {
                    return ScriptedHandler.Json(JsonSerializer.Serialize(Record("/a.txt", 8), ByteShelfJsonContext.Default.ShelfFile));
                }

                HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("tampered") };
                response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue($"\"{new string('0', 64)}\"");
                return response;
            });
            CliConfigStore store = new CliConfigStore(_work.File("config/config.json"));
            store.Save(new CliConfig { Server = "https://files.test/", Login = new KeyLogin { Key = "bsk_key" } });
            using ConfiguredClientSource clients = new ConfiguredClientSource(store, _ => null, server);

            CliRun run = await CliRun.RunAsync(clients, new[] { "get", "docs:/a.txt", "-" });

            // The bytes stream out as they arrive; the check can only fail once the last one has gone.
            Assert.Equal(CliApp.Failed, run.ExitCode);
            Assert.Equal("tampered", Encoding.UTF8.GetString(run.BinaryOutput));
            Assert.Contains("SHA-256", run.Error, StringComparison.Ordinal);
        }

        [Fact]
        public void ExitCodes_AreTheNumbersTheDocsGive()
        {
            Assert.Equal((0, 1, 2, 130), (CliApp.Succeeded, CliApp.Failed, CliApp.Misused, CliApp.Interrupted));
        }

        private Task<CliRun> RunAsync(params string[] commandLine)
        {
            return CliRun.RunAsync(_clients, commandLine);
        }

        private Task<ShelfFile> UploadAsync(string path, string text)
        {
            return Docs.UploadAsync(path, new MemoryStream(Encoding.UTF8.GetBytes(text)));
        }

        private static ShelfFile Record(string path, long sizeBytes)
        {
            return new ShelfFile
            {
                Id = Guid.NewGuid(),
                ShelfId = Guid.NewGuid(),
                DirectoryId = Guid.NewGuid(),
                Path = path,
                Name = ShelfPath.GetName(path),
                ContentType = "text/plain",
                SizeBytes = sizeBytes,
                Sha256 = new string('0', 64),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                Encrypted = false,
            };
        }

        /// <summary>Contents whose first read says it has begun, then waits to be let go.</summary>
        private sealed class HeldStream : MemoryStream
        {
            private readonly TaskCompletionSource _reading;
            private readonly Task _release;

            public HeldStream(byte[] content, TaskCompletionSource reading, Task release)
                : base(content)
            {
                _reading = reading;
                _release = release;
            }

            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                _reading.TrySetResult();
                await _release.WaitAsync(cancellationToken);
                return await base.ReadAsync(buffer, cancellationToken);
            }

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
            }
        }
    }
}
