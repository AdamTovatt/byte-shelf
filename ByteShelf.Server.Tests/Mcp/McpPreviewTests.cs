using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelf.Tools.Mcp;
using ByteShelfClient;
using ByteShelfCommon;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace ByteShelf.Server.Tests.Mcp
{
    /// <summary>What /mcp shows an agent of a file it cannot be answered as it is, where a plugin makes previews for agents.</summary>
    public sealed class McpPreviewTests : IAsyncLifetime
    {
        private readonly TestDerivedPlugins _plugins = new TestDerivedPlugins();
        private ServerTestHost _host = null!;

        public async Task InitializeAsync()
        {
            _plugins.All = [_plugins.Plugin("images", 1, [DerivedKind.Agent], "image/*", "application/pdf"), _plugins.Plugin("videos", 1, [DerivedKind.Thumbnail], "video/*")];
            _host = await ServerTestHost.StartAsync(_plugins.AddTo);
            await _host.CreateShelfAsync("docs");
        }

        public async Task DisposeAsync()
        {
            await _host.DisposeAsync();
            _plugins.Dispose();
        }

        [Fact]
        public async Task Connect_SaysWhichFilesComeAsPreviews()
        {
            await using McpClient client = await ConnectAsync();

            McpClientTool download = (await client.ListToolsAsync()).Single(tool => tool.Name == "download_file");

            Assert.EndsWith("Any other application/pdf, image/* file, of any size, comes as a preview of it you can see, with where to get it whole; any other, and one no preview could be made of, comes only with why and where to get it.", download.Description, StringComparison.Ordinal);
            Assert.Contains("Any other application/pdf, image/* file, of any size, comes as a preview of it you can see, with where", Regex.Replace(client.ServerInstructions!, @"\s+", " "), StringComparison.Ordinal);
        }

        [Fact]
        public async Task DownloadFile_TooLargeToAnswer_IsShownAsItsPreview()
        {
            byte[] large = new byte[ByteShelfMcpTools.MaxInlineImageBytes + 1];
            "picture"u8.CopyTo(large);
            await _host.PutAsAdminAsync("docs", "/a.heic", large, "image/heic");
            await using McpClient client = await ConnectAsync();

            CallToolResult read = await DownloadAsync(client, "/a.heic");

            ImageContentBlock image = Assert.Single(read.Content.OfType<ImageContentBlock>());
            Assert.Equal("image/jpeg", image.MimeType);
            Assert.Equal([0xFF, 0xD8, 0xFF], image.DecodedData.ToArray()[..3]);
            Assert.Equal(
                "/a.heic is image/heic of 1 MiB; this is a preview of it. For the whole file, make a link to it with share_file and fetch it from there, such as with curl.",
                TextOf(read));
        }

        [Fact]
        public async Task DownloadFile_AnImageTheAgentCanSee_IsAnsweredItself()
        {
            byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00];
            await _host.PutAsAdminAsync("docs", "/a.png", png, "image/png");
            await using McpClient client = await ConnectAsync();

            Assert.Equal(png, Assert.Single((await DownloadAsync(client, "/a.png")).Content.OfType<ImageContentBlock>()).DecodedData.ToArray());
            Assert.Empty(_plugins.Runs);
        }

        [Fact]
        public async Task DownloadDeleted_IsShownAsItsPreviewToo()
        {
            await _host.PutAsAdminAsync("docs", "/a.heic", [.. "picture"u8, 0xFF, 0xFE], "image/heic");
            TrashEntry deleted = (await (await _host.AdminClient.DeleteAsync("/api/shelves/docs/files?path=/a.heic")).Content.ReadFromJsonAsync<TrashEntry>())!;
            await using McpClient client = await ConnectAsync();

            CallToolResult read = await client.CallToolAsync("download_deleted", new Dictionary<string, object?> { ["deletionId"] = deleted.DeletionId });

            Assert.Equal("image/jpeg", Assert.Single(read.Content.OfType<ImageContentBlock>()).MimeType);
        }

        [Theory]
        [InlineData("throw", "no preview could be made of /a.heic (image/heic); make a link to it with share_file and fetch it from there, such as with curl.")]
        [InlineData("words", "the preview made of /a.heic is no image these tools can answer; make a link to it with share_file and fetch it from there, such as with curl.")]
        public async Task DownloadFile_WithoutAPreviewToShow_SaysWhyAndHowToFetchIt(string contents, string expected)
        {
            await _host.PutAsAdminAsync("docs", "/a.heic", [.. System.Text.Encoding.UTF8.GetBytes(contents), (byte)' ', 0xFF], "image/heic");
            await using McpClient client = await ConnectAsync();

            CallToolResult read = await DownloadAsync(client, "/a.heic");

            Assert.Equal(true, read.IsError);
            Assert.EndsWith(expected, TextOf(read), StringComparison.Ordinal);
        }

        [UnixFact]
        public async Task DownloadFile_AsThePluginCannotRun_SaysSoAndHowToFetchIt()
        {
            await _host.DisposeAsync();
            _plugins.Program = ShellProgram.Of("exit 1");
            _host = await ServerTestHost.StartAsync(_plugins.AddTo);
            await _host.CreateShelfAsync("docs");
            await _host.PutAsAdminAsync("docs", "/a.heic", [0xFF], "image/heic");
            await using McpClient client = await ConnectAsync();

            CallToolResult read = await DownloadAsync(client, "/a.heic");

            Assert.Equal(true, read.IsError);
            Assert.EndsWith("the server failed to make a preview of /a.heic; make a link to it with share_file and fetch it from there, such as with curl.", TextOf(read), StringComparison.Ordinal);
        }

        [Fact]
        public async Task DownloadFile_WhoseShelfHasNoRoomForAPreview_SaysSoAndHowToFetchIt()
        {
            byte[] contents = [.. "picture"u8, 0xFF];
            (await _host.AdminClient.PutAsJsonAsync("/api/shelves/docs", new UpdateShelfRequest { Name = "docs", QuotaBytes = contents.Length + 1 })).EnsureSuccessStatusCode();
            await _host.PutAsAdminAsync("docs", "/a.heic", contents, "image/heic");
            await using McpClient client = await ConnectAsync();

            CallToolResult read = await DownloadAsync(client, "/a.heic");

            Assert.Equal(true, read.IsError);
            Assert.EndsWith(
                "a shelf that holds the contents of /a.heic has no room in its quota for a preview of it; make a link to it with share_file and fetch it from there, such as with curl.",
                TextOf(read),
                StringComparison.Ordinal);
        }

        [Fact]
        public async Task DownloadFile_OfATypeNoPreviewIsMadeOf_SaysWhatItIsAndHowToFetchIt()
        {
            await _host.PutAsAdminAsync("docs", "/a.mp4", [0xFF, 0xFE], "video/mp4");
            await using McpClient client = await ConnectAsync();

            CallToolResult read = await DownloadAsync(client, "/a.mp4");

            Assert.Equal(true, read.IsError);
            Assert.Contains("/a.mp4 is neither UTF-8 text nor a", TextOf(read), StringComparison.Ordinal);
        }

        [Fact]
        public async Task HttpClient_OpenDerived_ReadsWhatThePluginMade()
        {
            ShelfFile file = await _host.PutAsAdminAsync("docs", "/a.heic", "picture"u8.ToArray(), "image/heic");
            using HttpClient http = _host.CreateClient();
            CreatedShelfKey key = await _host.CreateKeyAsync("docs", ShelfPermission.Read);
            ByteShelfClient.Http.ByteShelfHttpClient client = new ByteShelfClient.Http.ByteShelfHttpClient(http, new ByteShelfClient.Http.ShelfKeyCredentials(key.Secret));

            await using DerivedFileContent made = await client.Shelf("docs").OpenDerivedAsync(file.Id, DerivedKind.Agent);
            using MemoryStream copy = new MemoryStream();
            await made.Content.CopyToAsync(copy);

            Assert.Equal((TestMaker.MediaType, 0xFF), (made.ContentType, (int)copy.ToArray()[0]));
        }

        [Fact]
        public async Task HttpClient_OpenDerivedOfOneThereIsNoRoomFor_ThrowsQuotaExceeded()
        {
            byte[] picture = "picture"u8.ToArray();
            (await _host.AdminClient.PostAsJsonAsync("/api/shelves", new CreateShelfRequest { Name = "small", QuotaBytes = picture.Length })).EnsureSuccessStatusCode();
            ShelfFile file = await _host.PutAsAdminAsync("small", "/a.heic", picture, "image/heic");
            using HttpClient http = _host.CreateClient();
            CreatedShelfKey key = await _host.CreateKeyAsync("small", ShelfPermission.Read);
            IShelfClient shelf = new ByteShelfClient.Http.ByteShelfHttpClient(http, new ByteShelfClient.Http.ShelfKeyCredentials(key.Secret)).Shelf("small");

            ByteShelfException refused = await Assert.ThrowsAsync<ByteShelfException>(() => shelf.OpenDerivedAsync(file.Id, DerivedKind.Agent));

            Assert.Equal((HttpStatusCode.InsufficientStorage, ErrorCodes.QuotaExceeded), (refused.StatusCode, refused.Code));
        }

        private static Task<CallToolResult> DownloadAsync(McpClient client, string path)
        {
            return client.CallToolAsync("download_file", new Dictionary<string, object?> { ["path"] = path }).AsTask();
        }

        private static string TextOf(CallToolResult result)
        {
            return string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
        }

        private async Task<McpClient> ConnectAsync()
        {
            HttpClient client = _host.CreateClient((await _host.CreateKeyAsync("docs", ShelfPermission.Read)).Secret);
            return await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri("http://localhost/mcp") }, client, ownsHttpClient: true));
        }
    }
}
