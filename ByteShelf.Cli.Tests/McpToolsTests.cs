using System.ComponentModel;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using ByteShelf.Cli.Config;
using ByteShelf.Cli.Connection;
using ByteShelf.Cli.Mcp;
using ByteShelf.Cli.Tests.TestUtilities;
using ByteShelf.Tools;
using ByteShelf.Tools.Mcp;
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
            _tools = new ByteShelfMcpTools(_clients, ToolHost.Local);
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
        public void Instructions_NameEveryToolButTheAdminOnes()
        {
            IEnumerable<string> named = typeof(ByteShelfMcpTools).GetMethods()
                .Where(method => method.GetCustomAttribute<ToolNeedsAttribute>()?.Need is ToolNeed need && need != ToolNeed.Admin)
                .Select(method => method.GetCustomAttribute<McpServerToolAttribute>()!.Name!);

            Assert.All(named, tool => Assert.Matches($@"(?<!\w){Regex.Escape(tool)}(?!\w)", McpServerRunner.Instructions));
        }

        /// <summary>
        /// The limits the tool descriptions state, built from the constants they come from: an attribute's
        /// text is a constant, so it cannot be built from them, and this is what keeps it in step.
        /// </summary>
        public static TheoryData<string, string?, string> StatedLimits => new TheoryData<string, string?, string>
        {
            { "create_shelf", null, $"1 to {ShelfName.MaxLength} " },
            { "upload_file", "text", $"at most {TextFormat.Size(ByteShelfMcpTools.MaxInlineBytes)}" },
            { "upload_file", "base64", $"at most {TextFormat.Size(ByteShelfMcpTools.MaxInlineBytes)}" },
            { "upload_file", "description", $"up to {FileDescription.MaxLength} characters" },
            { "set_description", null, $"up to {FileDescription.MaxLength} characters" },
            { "download_file", null, $"at most {TextFormat.Size(ByteShelfMcpTools.MaxInlineBytes)}" },
            { "share_file", "expiresIn", $"{DownloadLink.DefaultLifetime.TotalHours}h by default, at most {DownloadLink.MaxLifetime.TotalDays}d" },
            { "search_files", "limit", $"1 to {FileSearch.MaxLimit}; {FileSearch.DefaultLimit} by default" },
            { "list_tree", "limit", $"1 to {TreeQuery.MaxLimit}; {TreeQuery.DefaultLimit} by default" },
        };

        [Theory]
        [MemberData(nameof(StatedLimits))]
        public void Descriptions_StateTheLimitsTheConstantsSet(string tool, string? parameter, string stated)
        {
            MethodInfo method = typeof(ByteShelfMcpTools).GetMethods().Single(candidate => candidate.GetCustomAttribute<McpServerToolAttribute>()?.Name == tool);
            ICustomAttributeProvider described = parameter == null ? method : method.GetParameters().Single(candidate => candidate.Name == parameter);

            string description = ((DescriptionAttribute)described.GetCustomAttributes(typeof(DescriptionAttribute), inherit: false).Single()).Description;

            Assert.Contains(stated, description, StringComparison.Ordinal);
        }

        [Fact]
        public void Instructions_StateTheLimitsTheConstantsSet()
        {
            string instructions = Regex.Replace(ToolInstructions.Text, @"\s+", " ");

            Assert.Contains($"up to {TextFormat.Size(ByteShelfMcpTools.MaxInlineBytes)}", instructions, StringComparison.Ordinal);
            Assert.Contains($"for {DownloadLink.DefaultLifetime.TotalHours} hours", instructions, StringComparison.Ordinal);
            Assert.Contains($"(at most {DownloadLink.MaxLifetime.TotalDays} days)", instructions, StringComparison.Ordinal);
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
        [InlineData(null, null, null)]
        [InlineData("/local.txt", "text", null)]
        [InlineData(null, "text", "dGV4dA==")]
        public async Task Upload_NotExactlyOneSource_IsAnError(string? localPath, string? text, string? base64)
        {
            McpException exception = await Assert.ThrowsAsync<McpException>(() => _tools.UploadFileAsync("docs", "/a.txt", localPath, text, base64));

            Assert.Contains("Give one of localPath, text or base64", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Upload_Base64_StoresTheBytes()
        {
            await _tools.UploadFileAsync("docs", "/a.bin", base64: "AP8K");

            await using Stream stored = await Docs.OpenReadAsync("/a.bin");
            using MemoryStream bytes = new MemoryStream();
            await stored.CopyToAsync(bytes);
            Assert.Equal(new byte[] { 0x00, 0xFF, 0x0A }, bytes.ToArray());
        }

        [Fact]
        public async Task Upload_TextOverTheCapInUtf8_IsAnErrorAndStoresNothing()
        {
            // Fewer characters than the cap, but two bytes each in UTF-8.
            string text = new string('ö', (ByteShelfMcpTools.MaxInlineBytes / 2) + 1);

            McpException exception = await Assert.ThrowsAsync<McpException>(() => _tools.UploadFileAsync("docs", "/a.txt", text: text));

            Assert.StartsWith($"text holds more than the {TextFormat.Size(ByteShelfMcpTools.MaxInlineBytes)}", exception.Message, StringComparison.Ordinal);
            await Assert.ThrowsAsync<ByteShelfException>(() => Docs.GetFileAsync("/a.txt"));
        }

        [Fact]
        public async Task Upload_Base64FarOverTheCap_IsRefusedWithoutDecoding()
        {
            // Not base64 at all, so only a refusal by length says it is too large.
            string base64 = new string('!', (2 * ByteShelfMcpTools.MaxInlineBytes) + 1);

            McpException exception = await Assert.ThrowsAsync<McpException>(() => _tools.UploadFileAsync("docs", "/a.bin", base64: base64));

            Assert.StartsWith("base64 holds more than the", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Upload_NotBase64_IsAnError()
        {
            McpException exception = await Assert.ThrowsAsync<McpException>(() => _tools.UploadFileAsync("docs", "/a.bin", base64: "not base64!"));

            Assert.StartsWith("base64 is not valid base64", exception.Message, StringComparison.Ordinal);
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

            Assert.Contains($"id           {file.Id:D}", ofFile, StringComparison.Ordinal);
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
        public async Task UploadWithDescription_ThenSearchByIt_FindsTheFile()
        {
            await _tools.UploadFileAsync("docs", "/scans/0042.pdf", text: "a", description: "Receipt for the new bike");
            await _tools.UploadFileAsync("docs", "/scans/0043.pdf", text: "b");

            string found = await _tools.SearchFilesAsync("docs", text: "bike");

            Assert.StartsWith("/scans/0042.pdf  1 B", found, StringComparison.Ordinal);
            Assert.DoesNotContain("0043", found, StringComparison.Ordinal);
        }

        [Fact]
        public async Task SearchFiles_MorePagesFollow_SaysWhatToPassAsAfter()
        {
            await _tools.UploadFileAsync("docs", "/a.txt", text: "a");
            await _tools.UploadFileAsync("docs", "/b.txt", text: "b");

            string first = await _tools.SearchFilesAsync("docs", limit: 1);
            string second = await _tools.SearchFilesAsync("docs", limit: 1, after: "/a.txt");

            Assert.EndsWith($"{Environment.NewLine}More follow: call again with after = /a.txt", first, StringComparison.Ordinal);
            Assert.StartsWith("/b.txt", second, StringComparison.Ordinal);
            Assert.DoesNotContain("More", second, StringComparison.Ordinal);
        }

        [Fact]
        public async Task SetDescription_Text_SetsItAndAnswersTheRecord()
        {
            await _tools.UploadFileAsync("docs", "/a.txt", text: "a");

            string described = await _tools.SetDescriptionAsync("docs", "/a.txt", "Draft");

            Assert.EndsWith("description  Draft", described, StringComparison.Ordinal);
            Assert.Equal("Draft", (await Docs.GetFileAsync("/a.txt")).Description);
        }

        [Fact]
        public async Task SetDescription_Without_RemovesIt()
        {
            await _tools.UploadFileAsync("docs", "/a.txt", text: "a", description: "Draft");

            await _tools.SetDescriptionAsync("docs", "/a.txt");

            Assert.Null((await Docs.GetFileAsync("/a.txt")).Description);
        }

        [Fact]
        public async Task ListTree_Pages_EndWithTheCursorToCallAgainWith()
        {
            await _tools.UploadFileAsync("docs", "/a/b.txt", text: "b");
            await _tools.UploadFileAsync("docs", "/c.txt", text: "c");

            string first = await _tools.ListTreeAsync("docs", limit: 2);
            string second = await _tools.ListTreeAsync("docs", limit: 2, after: "/a/b.txt");

            Assert.Equal(new[] { "/a/", "/a/b.txt  1 B", "More follow: call again with after = /a/b.txt" }, first.Split(Environment.NewLine));
            Assert.Equal("/c.txt  1 B", second);
        }

        [Fact]
        public async Task UploadFolderThenDownloadDirectory_KeepsTheTree()
        {
            Directory.CreateDirectory(_work.File("site/css"));
            Directory.CreateDirectory(_work.File("site/empty"));
            await File.WriteAllTextAsync(_work.File("site/index.html"), "<p>");
            await File.WriteAllTextAsync(_work.File("site/css/a.css"), "p{}");

            string uploaded = await _tools.UploadFolderAsync("docs", "/web/", _work.File("site"));
            string saved = await _tools.DownloadDirectoryAsync("docs", "/web/site", _work.File("copy"));

            Assert.Equal("Uploaded 2 files (6 B) to docs:/web/site.", uploaded);
            Assert.Equal($"Saved 2 files (6 B) to {_work.File("copy")}.", saved);
            Assert.Equal(("<p>", "p{}"), (await File.ReadAllTextAsync(_work.File("copy/index.html")), await File.ReadAllTextAsync(_work.File("copy/css/a.css"))));
            Assert.True(Directory.Exists(_work.File("copy/empty")));
        }

        [Fact]
        public async Task UploadFolder_StoppedByTheQuota_IsAnErrorSayingWhatItStoredFirst()
        {
            await _clients.Client.CreateShelfAsync("small", quotaBytes: 4);
            Directory.CreateDirectory(_work.File("site"));
            await File.WriteAllTextAsync(_work.File("site/a.txt"), "aaa");
            await File.WriteAllTextAsync(_work.File("site/b.txt"), "bbb");

            McpException exception = await Assert.ThrowsAsync<McpException>(() => _tools.UploadFolderAsync("small", "/", _work.File("site")));

            Assert.StartsWith($"Uploaded 1 file (3 B) to small:/site.{Environment.NewLine}Stopped before the end: ", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task DownloadDirectory_IntoAFolderThatExists_SavesItInsideUnderItsName()
        {
            await _tools.UploadFileAsync("docs", "/reports/a.txt", text: "a");

            await _tools.DownloadDirectoryAsync("docs", "/reports", _work.Path);

            Assert.Equal("a", await File.ReadAllTextAsync(_work.File("reports/a.txt")));
        }

        [Fact]
        public async Task DownloadDirectory_RelativeLocalPath_IsAnErrorAndSavesNothing()
        {
            await _tools.UploadFileAsync("docs", "/a/b.txt", text: "b");

            McpException exception = await Assert.ThrowsAsync<McpException>(() => _tools.DownloadDirectoryAsync("docs", "/a", "copy"));

            Assert.Equal("'copy' is relative; give an absolute local path.", exception.Message);
            Assert.False(Directory.Exists(Path.GetFullPath("copy")));
        }

        [Fact]
        public async Task ShareFile_ExpiresIn_AnswersTheLinkAndWhenItRunsOut()
        {
            await _tools.UploadFileAsync("docs", "/a.txt", text: "a");

            string shared = await _tools.ShareFileAsync("docs", "/a.txt", "30m");

            Assert.StartsWith("https://", shared, StringComparison.Ordinal);
            Assert.Contains("Works for anyone who has it until ", shared, StringComparison.Ordinal);
        }

        [Fact]
        public async Task ShareFile_ExpiresInNotALifetime_IsAnErrorSayingHowToWriteOne()
        {
            await _tools.UploadFileAsync("docs", "/a.txt", text: "a");

            McpException exception = await Assert.ThrowsAsync<McpException>(() => _tools.ShareFileAsync("docs", "/a.txt", "tomorrow"));

            Assert.Contains("30m, 12h or 7d", exception.Message, StringComparison.Ordinal);
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

            McpException exception = await Assert.ThrowsAsync<McpException>(() => new ByteShelfMcpTools(clients, ToolHost.Local).ListShelvesAsync(CancellationToken.None));

            Assert.Equal("Could not reach the server: No route to host.", exception.Message);
        }
    }
}
