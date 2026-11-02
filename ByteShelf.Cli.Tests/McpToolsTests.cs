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
using ByteShelfClient.Encryption;
using ByteShelfCommon;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
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
            { "download_file", null, $"WebP of at most {TextFormat.Size(ByteShelfMcpTools.MaxInlineImageBytes)}" },
            { "share_file", "expiresIn", $"{DownloadLink.Lifetime.Default.TotalHours}h by default, at most {DownloadLink.Lifetime.Max.TotalDays}d" },
            { "create_upload_link", "expiresIn", $"{UploadLink.Lifetime.Default.TotalMinutes}m by default, at most {UploadLink.Lifetime.Max.TotalHours}h" },
            { "create_upload_link", "description", $"up to {FileDescription.MaxLength} characters" },
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
            Assert.Contains($"image of up to {TextFormat.Size(ByteShelfMcpTools.MaxInlineImageBytes)}", instructions, StringComparison.Ordinal);
            Assert.Contains($"for {DownloadLink.Lifetime.Default.TotalHours} hours", instructions, StringComparison.Ordinal);
            Assert.Contains($"(at most {DownloadLink.Lifetime.Max.TotalDays} days)", instructions, StringComparison.Ordinal);
            Assert.Contains($"for {UploadLink.Lifetime.Default.TotalMinutes} minutes", instructions, StringComparison.Ordinal);
            Assert.Contains($"(at most {UploadLink.Lifetime.Max.TotalHours} hours)", instructions, StringComparison.Ordinal);
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
            await _tools.UploadFileAsync("/notes/todo.md", text: "- write tests", shelf: "docs");

            string text = TextOf(await _tools.DownloadFileAsync("/notes/todo.md", shelf: "docs"));

            Assert.Equal("- write tests", text);
            Assert.Equal("text/markdown", (await Docs.GetFileAsync("/notes/todo.md")).ContentType);
        }

        [Fact]
        public async Task UploadLocalFile_ToADirectoryPath_KeepsItsName()
        {
            await File.WriteAllTextAsync(_work.File("report.csv"), "a,b");

            await _tools.UploadFileAsync("/reports/", localPath: _work.File("report.csv"), shelf: "docs");

            Assert.Equal(3, (await Docs.GetFileAsync("/reports/report.csv")).SizeBytes);
        }

        [Fact]
        public async Task UploadText_ToADirectoryPath_IsAnError()
        {
            McpException exception = await Assert.ThrowsAsync<McpException>(() => _tools.UploadFileAsync("/reports/", text: "a,b", shelf: "docs"));

            Assert.StartsWith("Text has no file name", exception.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(null, null, null)]
        [InlineData("/local.txt", "text", null)]
        [InlineData(null, "text", "dGV4dA==")]
        public async Task Upload_NotExactlyOneSource_IsAnError(string? localPath, string? text, string? base64)
        {
            McpException exception = await Assert.ThrowsAsync<McpException>(() => _tools.UploadFileAsync("/a.txt", localPath, text, base64, shelf: "docs"));

            Assert.Contains("Give one of localPath, text or base64", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Upload_Base64_StoresTheBytes()
        {
            await _tools.UploadFileAsync("/a.bin", base64: "AP8K", shelf: "docs");

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

            McpException exception = await Assert.ThrowsAsync<McpException>(() => _tools.UploadFileAsync("/a.txt", text: text, shelf: "docs"));

            Assert.StartsWith($"text holds more than the {TextFormat.Size(ByteShelfMcpTools.MaxInlineBytes)}", exception.Message, StringComparison.Ordinal);
            await Assert.ThrowsAsync<ByteShelfException>(() => Docs.GetFileAsync("/a.txt"));
        }

        [Fact]
        public async Task Upload_Base64FarOverTheCap_IsRefusedWithoutDecoding()
        {
            // Not base64 at all, so only a refusal by length says it is too large.
            string base64 = new string('!', (2 * ByteShelfMcpTools.MaxInlineBytes) + 1);

            McpException exception = await Assert.ThrowsAsync<McpException>(() => _tools.UploadFileAsync("/a.bin", base64: base64, shelf: "docs"));

            Assert.StartsWith("base64 holds more than the", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Upload_NotBase64_IsAnError()
        {
            McpException exception = await Assert.ThrowsAsync<McpException>(() => _tools.UploadFileAsync("/a.bin", base64: "not base64!", shelf: "docs"));

            Assert.StartsWith("base64 is not valid base64", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task LocalPath_Relative_IsAnError()
        {
            await Docs.UploadAsync("/a.txt", new MemoryStream(Encoding.UTF8.GetBytes("a")));

            McpException upload = await Assert.ThrowsAsync<McpException>(() => _tools.UploadFileAsync("/b.txt", localPath: "b.txt", shelf: "docs"));
            McpException download = await Assert.ThrowsAsync<McpException>(() => _tools.DownloadFileAsync("/a.txt", localPath: "a.txt", shelf: "docs"));

            Assert.Contains("absolute", upload.Message, StringComparison.Ordinal);
            Assert.Contains("absolute", download.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task DownloadToALocalPath_SavesTheFile()
        {
            await Docs.UploadAsync("/a.txt", new MemoryStream(Encoding.UTF8.GetBytes("saved")));

            string answer = TextOf(await _tools.DownloadFileAsync("/a.txt", localPath: _work.Path, shelf: "docs"));

            Assert.Equal($"Saved {_work.File("a.txt")}", answer);
            Assert.Equal("saved", await File.ReadAllTextAsync(_work.File("a.txt")));
        }

        [Fact]
        public async Task DownloadInline_NotUtf8_IsAnErrorAskingForALocalPath()
        {
            await Docs.UploadAsync("/image.bin", new MemoryStream(new byte[] { 0xff, 0xfe, 0x00 }));

            McpException exception = await Assert.ThrowsAsync<McpException>(() => _tools.DownloadFileAsync("/image.bin", shelf: "docs"));

            Assert.Contains("local file", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task DownloadInline_LargerThanTheLimit_IsAnError()
        {
            byte[] large = Encoding.UTF8.GetBytes(new string('a', ByteShelfMcpTools.MaxInlineBytes + 1));
            await Docs.UploadAsync("/large.txt", new MemoryStream(large));

            McpException exception = await Assert.ThrowsAsync<McpException>(() => _tools.DownloadFileAsync("/large.txt", shelf: "docs"));

            Assert.Contains("more than the", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task DownloadInline_AnImage_AnswersItAsAnImageWithItsType()
        {
            byte[] png = { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0x00, 0xff };
            await Docs.UploadAsync("/photo.png", new MemoryStream(png), new UploadOptions { ContentType = "image/png" });

            List<ContentBlock> answer = (await _tools.DownloadFileAsync("/photo.png", shelf: "docs")).ToList();

            ImageContentBlock image = Assert.Single(answer.OfType<ImageContentBlock>());
            Assert.Equal("image/png", image.MimeType);
            Assert.Equal(png, image.DecodedData.ToArray());
            Assert.Contains("/photo.png", TextOf(answer), StringComparison.Ordinal);
        }

        [Fact]
        public async Task DownloadInline_AnImageOverItsCap_IsAnErrorPointingElsewhere()
        {
            await Docs.UploadAsync("/photo.jpg", new MemoryStream(new byte[ByteShelfMcpTools.MaxInlineImageBytes + 1]), new UploadOptions { ContentType = "image/jpeg" });

            McpException exception = await Assert.ThrowsAsync<McpException>(() => _tools.DownloadFileAsync("/photo.jpg", shelf: "docs"));

            Assert.Contains($"more than the {TextFormat.Size(ByteShelfMcpTools.MaxInlineImageBytes)} answered here as an image", exception.Message, StringComparison.Ordinal);
            Assert.Contains("share_file", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task ShelfLeftOut_ReachingOneShelf_WorksOnIt()
        {
            await _tools.UploadFileAsync("/a.txt", text: "only shelf");

            Assert.Equal("only shelf", TextOf(await _tools.DownloadFileAsync("/a.txt")));
        }

        [Fact]
        public async Task ShelfLeftOut_ReachingSeveralShelves_AsksForOneByName()
        {
            await _clients.Client.CreateShelfAsync("photos");

            McpException exception = await Assert.ThrowsAsync<McpException>(() => _tools.ListDirectoryAsync());

            Assert.Equal("Name the shelf: you reach docs, photos.", exception.Message);
        }

        [Fact]
        public async Task ShelfBlank_ReachingOneShelf_CountsAsLeftOut()
        {
            await _tools.UploadFileAsync("/a.txt", text: "blank shelf", shelf: "");

            Assert.Equal("blank shelf", TextOf(await _tools.DownloadFileAsync("/a.txt", shelf: " ")));
        }

        [Fact]
        public async Task ShelfLeftOut_ReachingNoShelf_SaysSo()
        {
            ByteShelfMcpTools tools = new ByteShelfMcpTools(new InMemoryClientSource(), ToolHost.Local);

            McpException exception = await Assert.ThrowsAsync<McpException>(() => tools.ListDirectoryAsync());

            Assert.Equal("You reach no shelf, so there is none to work on.", exception.Message);
        }

        [Fact]
        public async Task DownloadInline_TextStoredAsAnImage_AnswersTheText()
        {
            await Docs.UploadAsync("/notes.png", new MemoryStream(Encoding.UTF8.GetBytes("not a picture")), new UploadOptions { ContentType = "image/png" });

            List<ContentBlock> answer = (await _tools.DownloadFileAsync("/notes.png", shelf: "docs")).ToList();

            Assert.Empty(answer.OfType<ImageContentBlock>());
            Assert.Equal("not a picture", TextOf(answer));
        }

        [Fact]
        public async Task DownloadInline_AJpegStoredAsPng_AnswersItAsTheJpegItIs()
        {
            byte[] jpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 };
            await Docs.UploadAsync("/photo.png", new MemoryStream(jpeg), new UploadOptions { ContentType = "image/png" });

            ImageContentBlock image = Assert.Single((await _tools.DownloadFileAsync("/photo.png", shelf: "docs")).OfType<ImageContentBlock>());

            Assert.Equal("image/jpeg", image.MimeType);
        }

        [Fact]
        public async Task DownloadInline_StoredAsAnImageButNotOneAndOverTheTextCap_IsAnError()
        {
            await Docs.UploadAsync("/big.png", new MemoryStream(new byte[ByteShelfMcpTools.MaxInlineBytes + 1]), new UploadOptions { ContentType = "image/png" });

            McpException exception = await Assert.ThrowsAsync<McpException>(() => _tools.DownloadFileAsync("/big.png", shelf: "docs"));

            Assert.StartsWith($"/big.png is not a {ByteShelfMcpTools.InlineImageKinds} image", exception.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "image/png")]
        [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xDB }, "image/jpeg")]
        [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x37, 0x61 }, "image/gif")]
        [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }, "image/gif")]
        [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0x10, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50 }, "image/webp")]
        [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0x10, 0x00, 0x00, 0x00, 0x57, 0x41, 0x56, 0x45 }, null)]
        [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, null)]
        [InlineData(new byte[0], null)]
        public void ImageTypeOf_KnowsEachKindBySignature(byte[] content, string? expected)
        {
            Assert.Equal(expected, ByteShelfMcpTools.ImageTypeOf(content));
        }

        [Fact]
        public async Task CreateDirectory_ThenListDirectory_ShowsIt()
        {
            await _tools.CreateDirectoryAsync("/a/b", shelf: "docs", CancellationToken.None);

            string listing = await _tools.ListDirectoryAsync("/a", shelf: "docs");

            Assert.Equal($"/a  (1 directories, 0 files){Environment.NewLine}  b/  0 B in 0 files", listing);
        }

        [Fact]
        public async Task ListDirectory_ShowsWhatIsUnderEachDirectory()
        {
            await _tools.UploadFileAsync("/a/b/one.txt", text: "abc", shelf: "docs");
            await _tools.UploadFileAsync("/a/b/c/two.txt", text: "de", shelf: "docs");

            string listing = await _tools.ListDirectoryAsync("/a", shelf: "docs");

            Assert.Equal($"/a  (1 directories, 0 files){Environment.NewLine}  b/  5 B in 2 files", listing);
        }

        [Fact]
        public async Task GetInfo_AFileOrADirectory_DescribesIt()
        {
            ShelfFile file = await Docs.UploadAsync("/a/b.txt", new MemoryStream(Encoding.UTF8.GetBytes("b")));

            string ofFile = await _tools.GetInfoAsync("/a/b.txt", shelf: "docs", CancellationToken.None);
            string ofDirectory = await _tools.GetInfoAsync("/a", shelf: "docs", CancellationToken.None);

            Assert.Contains($"id           {file.Id:D}", ofFile, StringComparison.Ordinal);
            Assert.StartsWith("/a/  (id ", ofDirectory, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Move_AFile_MovesIt()
        {
            await _tools.UploadFileAsync("/a.txt", text: "a", shelf: "docs");

            string answer = await _tools.MoveAsync("/a.txt", "/b/c.txt", shelf: "docs", CancellationToken.None);

            Assert.Equal("Moved to /b/c.txt", answer);
            Assert.Equal("a", TextOf(await _tools.DownloadFileAsync("/b/c.txt", shelf: "docs")));
        }

        [Fact]
        public async Task Delete_ListTrash_Restore_PutsTheDirectoryBack()
        {
            await _tools.UploadFileAsync("/a/b.txt", text: "b", shelf: "docs");
            await _tools.DeleteAsync("/a", shelf: "docs", CancellationToken.None);
            TrashEntry entry = Assert.Single(await Docs.GetTrashAsync());

            string trash = await _tools.ListTrashAsync("docs", CancellationToken.None);
            await _tools.RestoreAsync(entry.DeletionId, shelf: "docs", CancellationToken.None);

            Assert.StartsWith(entry.DeletionId.ToString("D"), trash, StringComparison.Ordinal);
            Assert.Equal("b", TextOf(await _tools.DownloadFileAsync("/a/b.txt", shelf: "docs")));
        }

        [Fact]
        public async Task EmptyTrash_ForgetsEveryDeletion()
        {
            await _tools.UploadFileAsync("/a.txt", text: "a", shelf: "docs");
            await _tools.DeleteAsync("/a.txt", shelf: "docs", CancellationToken.None);

            await _tools.EmptyTrashAsync("docs", CancellationToken.None);

            Assert.Equal("The trash is empty.", await _tools.ListTrashAsync("docs", CancellationToken.None));
        }

        [Fact]
        public async Task UploadWithDescription_ThenSearchByIt_FindsTheFile()
        {
            await _tools.UploadFileAsync("/scans/0042.pdf", text: "a", description: "Receipt for the new bike", shelf: "docs");
            await _tools.UploadFileAsync("/scans/0043.pdf", text: "b", shelf: "docs");

            string found = await _tools.SearchFilesAsync(text: "bike", shelf: "docs");

            Assert.StartsWith("/scans/0042.pdf  1 B", found, StringComparison.Ordinal);
            Assert.DoesNotContain("0043", found, StringComparison.Ordinal);
        }

        [Fact]
        public async Task SearchFiles_MorePagesFollow_SaysWhatToPassAsAfter()
        {
            await _tools.UploadFileAsync("/a.txt", text: "a", shelf: "docs");
            await _tools.UploadFileAsync("/b.txt", text: "b", shelf: "docs");

            string first = await _tools.SearchFilesAsync(limit: 1, shelf: "docs");
            string second = await _tools.SearchFilesAsync(limit: 1, after: "/a.txt", shelf: "docs");

            Assert.EndsWith($"{Environment.NewLine}More follow: call again with after = /a.txt", first, StringComparison.Ordinal);
            Assert.StartsWith("/b.txt", second, StringComparison.Ordinal);
            Assert.DoesNotContain("More", second, StringComparison.Ordinal);
        }

        [Fact]
        public async Task SetDescription_Text_SetsItAndAnswersTheRecord()
        {
            await _tools.UploadFileAsync("/a.txt", text: "a", shelf: "docs");

            string described = await _tools.SetDescriptionAsync("/a.txt", "Draft", shelf: "docs");

            Assert.EndsWith("description  Draft", described, StringComparison.Ordinal);
            Assert.Equal("Draft", (await Docs.GetFileAsync("/a.txt")).Description);
        }

        [Fact]
        public async Task SetDescription_Without_RemovesIt()
        {
            await _tools.UploadFileAsync("/a.txt", text: "a", description: "Draft", shelf: "docs");

            await _tools.SetDescriptionAsync("/a.txt", shelf: "docs");

            Assert.Null((await Docs.GetFileAsync("/a.txt")).Description);
        }

        [Fact]
        public async Task ListTree_Pages_EndWithTheCursorToCallAgainWith()
        {
            await _tools.UploadFileAsync("/a/b.txt", text: "b", shelf: "docs");
            await _tools.UploadFileAsync("/c.txt", text: "c", shelf: "docs");

            string first = await _tools.ListTreeAsync(limit: 2, shelf: "docs");
            string second = await _tools.ListTreeAsync(limit: 2, after: "/a/b.txt", shelf: "docs");

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

            string uploaded = await _tools.UploadFolderAsync("/web/", _work.File("site"), shelf: "docs");
            string saved = await _tools.DownloadDirectoryAsync("/web/site", _work.File("copy"), shelf: "docs");

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

            McpException exception = await Assert.ThrowsAsync<McpException>(() => _tools.UploadFolderAsync("/", _work.File("site"), shelf: "small"));

            Assert.StartsWith($"Uploaded 1 file (3 B) to small:/site.{Environment.NewLine}Stopped before the end: ", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task DownloadDirectory_IntoAFolderThatExists_SavesItInsideUnderItsName()
        {
            await _tools.UploadFileAsync("/reports/a.txt", text: "a", shelf: "docs");

            await _tools.DownloadDirectoryAsync("/reports", _work.Path, shelf: "docs");

            Assert.Equal("a", await File.ReadAllTextAsync(_work.File("reports/a.txt")));
        }

        [Fact]
        public async Task DownloadDirectory_RelativeLocalPath_IsAnErrorAndSavesNothing()
        {
            await _tools.UploadFileAsync("/a/b.txt", text: "b", shelf: "docs");

            McpException exception = await Assert.ThrowsAsync<McpException>(() => _tools.DownloadDirectoryAsync("/a", "copy", shelf: "docs"));

            Assert.Equal("'copy' is relative; give an absolute local path.", exception.Message);
            Assert.False(Directory.Exists(Path.GetFullPath("copy")));
        }

        [Fact]
        public async Task ShareFile_ExpiresIn_AnswersTheLinkAndWhenItRunsOut()
        {
            await _tools.UploadFileAsync("/a.txt", text: "a", shelf: "docs");

            string shared = await _tools.ShareFileAsync("/a.txt", "30m", shelf: "docs");

            Assert.StartsWith("https://", shared, StringComparison.Ordinal);
            Assert.Contains("Works for anyone who has it until ", shared, StringComparison.Ordinal);
        }

        [Fact]
        public async Task CreateUploadLink_AnswersTheLinkWhatItStoresAndTheCurlCommand()
        {
            string answer = await _tools.CreateUploadLinkAsync("/in/q3.pdf", expiresIn: "2h", shelf: "docs");

            string[] lines = answer.Split(Environment.NewLine);
            Assert.StartsWith("https://", lines[0], StringComparison.Ordinal);
            Assert.StartsWith("Stores one file at /in/q3.pdf for anyone who has it until ", lines[1], StringComparison.Ordinal);
            Assert.Equal($"curl -fsS -T <local file> '{lines[0]}'", lines[^1]);
        }

        [Fact]
        public async Task CreateUploadLink_ExpiresInPastTheMost_IsAnErrorNamingTheBounds()
        {
            McpException exception = await Assert.ThrowsAsync<McpException>(() => _tools.CreateUploadLinkAsync("/a.txt", expiresIn: "2d", shelf: "docs"));

            Assert.Contains("from 1 minute to 24 hours", exception.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task DownloadFile_Encrypted_IsRefusedSayingPasswordsNeverGoThroughToolCalls(bool toLocalFile)
        {
            await StoreEncryptedAsync("/secret.txt");

            McpException exception = await Assert.ThrowsAsync<McpException>(() => _tools.DownloadFileAsync("/secret.txt", localPath: toLocalFile ? _work.Path : null, shelf: "docs"));

            Assert.Contains("passwords never go through tool calls", exception.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(_work.File("secret.txt")));
        }

        [Fact]
        public async Task DownloadDirectory_EncryptedFile_IsLeftOutAndTheRestSaved()
        {
            await StoreEncryptedAsync("/mixed/secret.txt");
            await _tools.UploadFileAsync("/mixed/plain.txt", text: "plain", shelf: "docs");

            string answer = await _tools.DownloadDirectoryAsync("/mixed", _work.Path, shelf: "docs");

            Assert.Contains("secret.txt: it is encrypted with a password", answer, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(_work.Path, "mixed", "plain.txt")));
            Assert.Empty(Directory.GetFiles(Path.Combine(_work.Path, "mixed"), "secret*"));
        }

        [Fact]
        public async Task ListDirectory_MarksWhatIsEncrypted()
        {
            await StoreEncryptedAsync("/a/secret.txt");

            string listing = await _tools.ListDirectoryAsync("/a", shelf: "docs");

            Assert.EndsWith("(encrypted)", listing.Split(Environment.NewLine)[1], StringComparison.Ordinal);
        }

        [Fact]
        public async Task ShareFile_ExpiresInNotALifetime_IsAnErrorSayingHowToWriteOne()
        {
            await _tools.UploadFileAsync("/a.txt", text: "a", shelf: "docs");

            McpException exception = await Assert.ThrowsAsync<McpException>(() => _tools.ShareFileAsync("/a.txt", "tomorrow", shelf: "docs"));

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

        private Task<ShelfFile> StoreEncryptedAsync(string path)
        {
            return Transfers.UploadAsync(Docs, path, new MemoryStream(Encoding.UTF8.GetBytes("secret")), new UploadOptions(), new Encryptor("pw", 10), CancellationToken.None);
        }

        private static string TextOf(IEnumerable<ContentBlock> answer)
        {
            return string.Concat(answer.OfType<TextContentBlock>().Select(block => block.Text));
        }
    }
}
