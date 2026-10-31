using System.ComponentModel;
using System.Reflection;
using System.Text;
using ByteShelf.Cli.Config;
using ByteShelf.Cli.Connection;
using ByteShelf.Cli.Mcp;
using ByteShelf.Cli.Tests.TestUtilities;
using ByteShelfClient;
using ByteShelfCommon;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace ByteShelf.Cli.Tests
{
    /// <summary>The MCP tools over an in-memory client.</summary>
    public sealed class McpToolsTests : IAsyncLifetime, IDisposable
    {
        private readonly InMemoryClientSource _clients = new InMemoryClientSource();
        private readonly WorkFolder _work = new WorkFolder();
        private ByteShelfMcpTools _tools = null!;

        private IShelfClient Docs => _clients.Client.Shelf("docs");

        public async Task InitializeAsync()
        {
            await _clients.Client.CreateShelfAsync("docs");
            _tools = new ByteShelfMcpTools(_clients);
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
        public void EveryTool_HasItsOwnNameAndADescription()
        {
            List<MethodInfo> tools = typeof(ByteShelfMcpTools).GetMethods()
                .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() != null)
                .ToList();

            Assert.NotEmpty(tools);
            Assert.All(tools, tool => Assert.False(string.IsNullOrWhiteSpace(tool.GetCustomAttribute<DescriptionAttribute>()?.Description)));
            Assert.Equal(tools.Count, tools.Select(tool => tool.GetCustomAttribute<McpServerToolAttribute>()!.Name).Distinct().Count());
        }

        [Fact]
        public async Task ListShelves_AndCreateShelf_ShowTheNewShelf()
        {
            await _tools.CreateShelfAsync("photos", quotaBytes: 2048);

            string shelves = await _tools.ListShelvesAsync(CancellationToken.None);

            Assert.Equal($"docs  0 B used  0 files{Environment.NewLine}photos  0 B of 2 KiB used  0 files", shelves);
        }

        [Fact]
        public async Task UploadText_ThenDownloadInline_AnswersTheText()
        {
            await _tools.UploadFileAsync("docs", "/notes/todo.md", text: "- write tests");

            string text = await _tools.DownloadFileAsync("docs", "/notes/todo.md");

            Assert.Equal("- write tests", text);
            Assert.Equal("text/markdown", (await Docs.GetFileAsync("/notes/todo.md")).ContentType);
        }

        [Fact]
        public async Task UploadLocalFile_ToADirectoryPath_KeepsItsName()
        {
            await File.WriteAllTextAsync(_work.File("report.csv"), "a,b");

            await _tools.UploadFileAsync("docs", "/reports/", localPath: _work.File("report.csv"));

            Assert.Equal(3, (await Docs.GetFileAsync("/reports/report.csv")).SizeBytes);
        }

        [Fact]
        public async Task UploadText_ToADirectoryPath_IsAnError()
        {
            McpException exception = await Assert.ThrowsAsync<McpException>(() => _tools.UploadFileAsync("docs", "/reports/", text: "a,b"));

            Assert.StartsWith("Text has no file name", exception.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(null, null)]
        [InlineData("/local.txt", "text")]
        public async Task Upload_NotExactlyOneSource_IsAnError(string? localPath, string? text)
        {
            McpException exception = await Assert.ThrowsAsync<McpException>(() => _tools.UploadFileAsync("docs", "/a.txt", localPath, text));

            Assert.Contains("one of them", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task LocalPath_Relative_IsAnError()
        {
            await Docs.UploadAsync("/a.txt", new MemoryStream(Encoding.UTF8.GetBytes("a")));

            McpException upload = await Assert.ThrowsAsync<McpException>(() => _tools.UploadFileAsync("docs", "/b.txt", localPath: "b.txt"));
            McpException download = await Assert.ThrowsAsync<McpException>(() => _tools.DownloadFileAsync("docs", "/a.txt", localPath: "a.txt"));

            Assert.Contains("absolute", upload.Message, StringComparison.Ordinal);
            Assert.Contains("absolute", download.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task DownloadToALocalPath_SavesTheFile()
        {
            await Docs.UploadAsync("/a.txt", new MemoryStream(Encoding.UTF8.GetBytes("saved")));

            string answer = await _tools.DownloadFileAsync("docs", "/a.txt", localPath: _work.Path);

            Assert.Equal($"Saved {_work.File("a.txt")}", answer);
            Assert.Equal("saved", await File.ReadAllTextAsync(_work.File("a.txt")));
        }

        [Fact]
        public async Task DownloadInline_NotUtf8_IsAnErrorAskingForALocalPath()
        {
            await Docs.UploadAsync("/image.bin", new MemoryStream(new byte[] { 0xff, 0xfe, 0x00 }));

            McpException exception = await Assert.ThrowsAsync<McpException>(() => _tools.DownloadFileAsync("docs", "/image.bin"));

            Assert.Contains("local file", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task DownloadInline_LargerThanTheLimit_IsAnError()
        {
            byte[] large = Encoding.UTF8.GetBytes(new string('a', ByteShelfMcpTools.MaxInlineBytes + 1));
            await Docs.UploadAsync("/large.txt", new MemoryStream(large));

            McpException exception = await Assert.ThrowsAsync<McpException>(() => _tools.DownloadFileAsync("docs", "/large.txt"));

            Assert.Contains("more than the", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task CreateDirectory_ThenListDirectory_ShowsIt()
        {
            await _tools.CreateDirectoryAsync("docs", "/a/b", CancellationToken.None);

            string listing = await _tools.ListDirectoryAsync("docs", "/a");

            Assert.Equal($"/a  (1 directories, 0 files){Environment.NewLine}  b/", listing);
        }

        [Fact]
        public async Task GetInfo_AFileOrADirectory_DescribesIt()
        {
            ShelfFile file = await Docs.UploadAsync("/a/b.txt", new MemoryStream(Encoding.UTF8.GetBytes("b")));

            string ofFile = await _tools.GetInfoAsync("docs", "/a/b.txt", CancellationToken.None);
            string ofDirectory = await _tools.GetInfoAsync("docs", "/a", CancellationToken.None);

            Assert.Contains($"id       {file.Id:D}", ofFile, StringComparison.Ordinal);
            Assert.StartsWith("/a/  (id ", ofDirectory, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Move_AFile_MovesIt()
        {
            await _tools.UploadFileAsync("docs", "/a.txt", text: "a");

            string answer = await _tools.MoveAsync("docs", "/a.txt", "/b/c.txt", CancellationToken.None);

            Assert.Equal("Moved to /b/c.txt", answer);
            Assert.Equal("a", await _tools.DownloadFileAsync("docs", "/b/c.txt"));
        }

        [Fact]
        public async Task Delete_ListTrash_Restore_PutsTheDirectoryBack()
        {
            await _tools.UploadFileAsync("docs", "/a/b.txt", text: "b");
            await _tools.DeleteAsync("docs", "/a", CancellationToken.None);
            TrashEntry entry = Assert.Single(await Docs.GetTrashAsync());

            string trash = await _tools.ListTrashAsync("docs", CancellationToken.None);
            await _tools.RestoreAsync("docs", entry.DeletionId, CancellationToken.None);

            Assert.StartsWith(entry.DeletionId.ToString("D"), trash, StringComparison.Ordinal);
            Assert.Equal("b", await _tools.DownloadFileAsync("docs", "/a/b.txt"));
        }

        [Fact]
        public async Task EmptyTrash_ForgetsEveryDeletion()
        {
            await _tools.UploadFileAsync("docs", "/a.txt", text: "a");
            await _tools.DeleteAsync("docs", "/a.txt", CancellationToken.None);

            await _tools.EmptyTrashAsync("docs", CancellationToken.None);

            Assert.Equal("The trash is empty.", await _tools.ListTrashAsync("docs", CancellationToken.None));
        }

        [Fact]
        public async Task ServerRefusal_IsAnErrorWithItsCode()
        {
            McpException exception = await Assert.ThrowsAsync<McpException>(() => _tools.ShelfInfoAsync("missing", CancellationToken.None));

            Assert.EndsWith($"({ErrorCodes.NotFound})", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task ServerUnreachable_IsAnErrorSayingSo()
        {
            using ScriptedHandler unreachable = new ScriptedHandler(_ => throw new HttpRequestException("No route to host."));
            CliConfigStore store = new CliConfigStore(_work.File("config/config.json"));
            store.Save(new CliConfig { Server = "https://files.test/", Login = new KeyLogin { Key = "bsk_key" } });
            using ConfiguredClientSource clients = new ConfiguredClientSource(store, _ => null, unreachable);

            McpException exception = await Assert.ThrowsAsync<McpException>(() => new ByteShelfMcpTools(clients).ListShelvesAsync(CancellationToken.None));

            Assert.Equal("Could not reach the server: No route to host.", exception.Message);
        }
    }
}
