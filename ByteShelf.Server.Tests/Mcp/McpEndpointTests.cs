using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using ByteShelf.Tools;
using ByteShelf.Tools.Mcp;
using ByteShelf.Server.Replication.Models;
using ByteShelf.Server.Replication.Repositories;
using ByteShelf.Server.Shared;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace ByteShelf.Server.Tests.Mcp
{
    /// <summary>The server's /mcp: the CLI's tools, over Streamable HTTP, for a shelf key.</summary>
    public sealed class McpEndpointTests : IAsyncLifetime
    {
        private static readonly string[] ReadTools =
        {
            "download_file", "get_info", "list_directory", "list_shelves", "list_trash", "list_tree", "search_files", "share_file", "shelf_info",
        };

        private static readonly string[] AddTools = { "create_directory", "create_upload_link", "upload_file" };

        private static readonly string[] WriteTools = { "delete", "empty_trash", "move", "restore", "set_description" };

        private ServerTestHost _host = null!;

        public async Task InitializeAsync()
        {
            _host = await ServerTestHost.StartAsync();
            (await _host.AdminClient.PostAsJsonAsync("/api/shelves", new CreateShelfRequest { Name = "docs" })).EnsureSuccessStatusCode();
            (await _host.AdminClient.PostAsJsonAsync("/api/shelves", new CreateShelfRequest { Name = "other" })).EnsureSuccessStatusCode();
        }

        public async Task DisposeAsync()
        {
            await _host.DisposeAsync();
        }

        [Theory]
        [InlineData(ShelfKeyPermission.Read)]
        [InlineData(ShelfKeyPermission.AddOnly)]
        [InlineData(ShelfKeyPermission.ReadWrite)]
        public async Task ListTools_ForAKey_OffersWhatItsPermissionAllowsAndNeverAdminOrLocalFileTools(ShelfKeyPermission permission)
        {
            await using McpClient client = await ConnectAsync(await CreateKeyAsync("docs", permission));

            IList<McpClientTool> tools = await client.ListToolsAsync();

            IEnumerable<string> expected = permission switch
            {
                ShelfKeyPermission.Read => ReadTools,
                ShelfKeyPermission.AddOnly => ReadTools.Concat(AddTools),
                _ => ReadTools.Concat(AddTools).Concat(WriteTools),
            };
            Assert.Equal(expected.Order(StringComparer.Ordinal), tools.Select(tool => tool.Name).Order(StringComparer.Ordinal));
        }

        [Fact]
        public async Task Connect_Instructions_NameTheKeysShelfAndAccess()
        {
            await using McpClient client = await ConnectAsync(await CreateKeyAsync("docs", ShelfKeyPermission.AddOnly));

            Assert.Contains("one shelf, \"docs\", which every tool works on when you leave shelf out", Regex.Replace(client.ServerInstructions!, @"\s+", " "), StringComparison.Ordinal);
            Assert.Contains("The key's access is add-only", client.ServerInstructions, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Connect_Instructions_NameEveryToolOffered()
        {
            await using McpClient client = await ConnectAsync(await CreateKeyAsync("docs", ShelfKeyPermission.ReadWrite));

            IList<McpClientTool> tools = await client.ListToolsAsync();

            Assert.All(tools, tool => Assert.Matches($@"(?<!\w){Regex.Escape(tool.Name)}(?!\w)", client.ServerInstructions));
        }

        [Fact]
        public async Task ListTools_OnAReplica_OffersOnlyTheReadingToolsAndSaysWhy()
        {
            string secret = await CreateKeyAsync("docs", ShelfKeyPermission.ReadWrite);
            using (IServiceScope scope = _host.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<IReplicationSourceRepository>().TryInsertAsync(new ReplicationSource
                {
                    PrimaryUrl = "http://primary.invalid/",
                    PrimaryInstanceId = Guid.NewGuid().ToString("D"),
                    Credential = "replica-credential",
                    PairedAt = DateTimeOffset.UtcNow,
                    AppliedSequence = 0,
                    NeedsCopy = false,
                });
            }

            await using McpClient client = await ConnectAsync(secret);

            Assert.Equal(ReadTools.Order(StringComparer.Ordinal), (await client.ListToolsAsync()).Select(tool => tool.Name).Order(StringComparer.Ordinal));
            Assert.Contains("This server is a replica, a read-only copy of another", client.ServerInstructions, StringComparison.Ordinal);
            Assert.DoesNotContain("The key's access is", client.ServerInstructions, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task CallTools_WithTheKeyAsBearerOrHeader_ActOnTheKeysShelf(bool asBearer)
        {
            await using McpClient client = await ConnectAsync(await CreateKeyAsync("docs", ShelfKeyPermission.ReadWrite), asBearer);

            CallToolResult stored = await client.CallToolAsync("upload_file", new Dictionary<string, object?> { ["shelf"] = "docs", ["path"] = "/notes/a.txt", ["text"] = "hello" });
            CallToolResult read = await client.CallToolAsync("download_file", new Dictionary<string, object?> { ["shelf"] = "docs", ["path"] = "/notes/a.txt" });

            AssertSucceeded(stored);
            Assert.Equal("hello", TextOf(read));
            Assert.Equal("hello", await _host.AdminClient.GetStringAsync("/api/shelves/docs/content?path=/notes/a.txt"));
        }

        [Fact]
        public async Task CallTools_ShelfLeftOut_ActOnTheKeysShelf()
        {
            await using McpClient client = await ConnectAsync(await CreateKeyAsync("docs", ShelfKeyPermission.ReadWrite));

            AssertSucceeded(await client.CallToolAsync("upload_file", new Dictionary<string, object?> { ["path"] = "/a.txt", ["text"] = "keys shelf" }));
            CallToolResult read = await client.CallToolAsync("download_file", new Dictionary<string, object?> { ["path"] = "/a.txt" });

            Assert.Equal("keys shelf", TextOf(read));
            Assert.Equal("keys shelf", await _host.AdminClient.GetStringAsync("/api/shelves/docs/content?path=/a.txt"));
        }

        [Fact]
        public async Task CallTool_DownloadABinaryFile_PointsToShareFile()
        {
            await using McpClient client = await ConnectAsync(await CreateKeyAsync("docs", ShelfKeyPermission.ReadWrite));
            AssertSucceeded(await client.CallToolAsync("upload_file", new Dictionary<string, object?> { ["path"] = "/a.bin", ["base64"] = Convert.ToBase64String(new byte[] { 0xff, 0xfe, 0x00 }) }));

            CallToolResult read = await client.CallToolAsync("download_file", new Dictionary<string, object?> { ["path"] = "/a.bin" });

            Assert.Equal(true, read.IsError);
            Assert.Contains("make a link to it with share_file", TextOf(read), StringComparison.Ordinal);
            Assert.DoesNotContain("local file", TextOf(read), StringComparison.Ordinal);
        }

        [Fact]
        public async Task CallTool_DownloadAnImage_AnswersItAsAnImage()
        {
            byte[] png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0xFF };
            await using McpClient client = await ConnectAsync(await CreateKeyAsync("docs", ShelfKeyPermission.ReadWrite));
            AssertSucceeded(await client.CallToolAsync("upload_file", new Dictionary<string, object?> { ["path"] = "/a.png", ["base64"] = Convert.ToBase64String(png) }));

            CallToolResult read = await client.CallToolAsync("download_file", new Dictionary<string, object?> { ["path"] = "/a.png" });

            AssertSucceeded(read);
            ImageContentBlock image = Assert.Single(read.Content.OfType<ImageContentBlock>());
            Assert.Equal("image/png", image.MimeType);
            Assert.Equal(png, image.DecodedData.ToArray());
        }

        [Fact]
        public async Task CallTool_MissingARequiredArgument_NamesIt()
        {
            await using McpClient client = await ConnectAsync(await CreateKeyAsync("docs", ShelfKeyPermission.ReadWrite));

            CallToolResult moved = await client.CallToolAsync("move", new Dictionary<string, object?> { ["path"] = "/a.txt" });

            Assert.Equal(true, moved.IsError);
            Assert.Equal("move needs toPath.", TextOf(moved));
        }

        [Fact]
        public async Task CallTool_UploadBase64_StoresTheBytes()
        {
            byte[] bytes = { 0x89, 0x50, 0x4E, 0x47, 0x00, 0xFF, 0xFE, 0x0A };
            await using McpClient client = await ConnectAsync(await CreateKeyAsync("docs", ShelfKeyPermission.AddOnly));

            CallToolResult stored = await client.CallToolAsync("upload_file", new Dictionary<string, object?> { ["shelf"] = "docs", ["path"] = "/pics/a.png", ["base64"] = Convert.ToBase64String(bytes) });

            AssertSucceeded(stored);
            Assert.Equal(bytes, await _host.AdminClient.GetByteArrayAsync("/api/shelves/docs/content?path=/pics/a.png"));
        }

        [Fact]
        public async Task CallTool_UploadBase64OverTheCap_IsRefusedAndStoresNothing()
        {
            await using McpClient client = await ConnectAsync(await CreateKeyAsync("docs", ShelfKeyPermission.AddOnly));

            CallToolResult result = await client.CallToolAsync(
                "upload_file", new Dictionary<string, object?> { ["shelf"] = "docs", ["path"] = "/big.bin", ["base64"] = Convert.ToBase64String(new byte[ByteShelfMcpTools.MaxInlineBytes + 1]) });

            Assert.Equal(true, result.IsError);
            Assert.Contains($"more than the {TextFormat.Size(ByteShelfMcpTools.MaxInlineBytes)}", TextOf(result), StringComparison.Ordinal);
            Assert.Equal(HttpStatusCode.NotFound, (await _host.AdminClient.GetAsync("/api/shelves/docs/content?path=/big.bin")).StatusCode);
        }

        [Fact]
        public async Task CallTool_ShareFileWithoutAPublicUrl_AnswersALinkOnTheAddressTheAgentCalledThatServesTheFile()
        {
            // The tools reach the API at http://localhost/ (the test's self address), so a link put on
            // that one rather than on the address the agent called shows.
            Uri publicAddress = new Uri("http://shelf.example.test/");
            await using McpClient client = await ConnectAsync(await CreateKeyAsync("docs", ShelfKeyPermission.ReadWrite), endpoint: new Uri(publicAddress, "mcp"));
            AssertSucceeded(await client.CallToolAsync("upload_file", new Dictionary<string, object?> { ["shelf"] = "docs", ["path"] = "/a.txt", ["text"] = "shared" }));

            CallToolResult shared = await client.CallToolAsync("share_file", new Dictionary<string, object?> { ["shelf"] = "docs", ["path"] = "/a.txt" });

            AssertSucceeded(shared);
            Uri link = new Uri(TextOf(shared).Split(Environment.NewLine)[0]);
            Assert.Equal(publicAddress.GetLeftPart(UriPartial.Authority), link.GetLeftPart(UriPartial.Authority));
            Assert.StartsWith("/api/downloads/", link.AbsolutePath, StringComparison.Ordinal);
            Assert.Equal("shared", await _host.AnonymousClient.GetStringAsync(link.PathAndQuery));
        }

        [Fact]
        public async Task CallTool_CreateUploadLinkWithAnAddOnlyKey_AnswersALinkThatStoresTheFileWithoutTheKey()
        {
            Uri publicAddress = new Uri("http://shelf.example.test/");
            await using McpClient client = await ConnectAsync(await CreateKeyAsync("docs", ShelfKeyPermission.AddOnly), endpoint: new Uri(publicAddress, "mcp"));

            CallToolResult created = await client.CallToolAsync(
                "create_upload_link", new Dictionary<string, object?> { ["path"] = "/in/q3.pdf", ["description"] = "Q3 report" });

            AssertSucceeded(created);
            Uri link = new Uri(TextOf(created).Split(Environment.NewLine)[0]);
            Assert.Equal(publicAddress.GetLeftPart(UriPartial.Authority), link.GetLeftPart(UriPartial.Authority));
            (await _host.AnonymousClient.PutAsync(link.PathAndQuery, new ByteArrayContent(new byte[] { 1, 2, 3 }))).EnsureSuccessStatusCode();
            ShelfFile stored = (await _host.AdminClient.GetFromJsonAsync<ShelfFile>("/api/shelves/docs/files?path=/in/q3.pdf"))!;
            Assert.Equal((3L, "application/pdf", "Q3 report"), (stored.SizeBytes, stored.ContentType, stored.Description));
        }

        [Theory]
        [InlineData(ShelfKeyPermission.Read, false, null)]
        [InlineData(ShelfKeyPermission.AddOnly, true, "never over a file already there")]
        [InlineData(ShelfKeyPermission.ReadWrite, true, "add &overwrite=true to replace a file")]
        public async Task Instructions_GiveCurlCommandsForWhatTheKeyMayDoOnTheAddressTheAgentCalled(ShelfKeyPermission permission, bool stores, string? overwriting)
        {
            await using McpClient client = await ConnectAsync(await CreateKeyAsync("docs", permission), endpoint: new Uri("http://shelf.example.test/mcp"));
            string instructions = client.ServerInstructions!;
            string content = "\"http://shelf.example.test/api/shelves/docs/content?path=/reports/q3.pdf\"";

            Assert.Contains($"-o q3.pdf {content}", instructions, StringComparison.Ordinal);
            Assert.Equal(stores, instructions.Contains($"-T ./q3.pdf {content}", StringComparison.Ordinal));
            Assert.Equal(stores, instructions.Contains("give upload_file the contents", StringComparison.Ordinal));
            Assert.Equal(permission == ShelfKeyPermission.ReadWrite, instructions.Contains("&overwrite=true", StringComparison.Ordinal));
            if (overwriting != null)
            {
                Assert.Contains(overwriting, instructions, StringComparison.Ordinal);
            }
        }

        [Fact]
        public async Task Instructions_WithAPublicUrl_GiveCurlCommandsOnIt()
        {
            await using ServerTestHost host = await ServerTestHost.StartAsync(services => services.AddSingleton(new PublicAddress("https://files.example.com/shelf")));
            await host.CreateShelfAsync("docs");
            string secret = (await host.CreateKeyAsync("docs", ShelfKeyPermission.Read)).Secret;

            await using McpClient client = await ConnectAsync(secret, endpoint: new Uri("http://shelf.example.test/mcp"), host: host);

            Assert.Contains("-o q3.pdf \"https://files.example.com/shelf/api/shelves/docs/content?path=/reports/q3.pdf\"", client.ServerInstructions, StringComparison.Ordinal);
        }

        [Fact]
        public async Task CallTool_AnotherShelf_IsNotFound()
        {
            await using McpClient client = await ConnectAsync(await CreateKeyAsync("docs", ShelfKeyPermission.ReadWrite));

            CallToolResult result = await client.CallToolAsync("list_directory", new Dictionary<string, object?> { ["shelf"] = "other" });

            Assert.Equal(true, result.IsError);
            Assert.EndsWith($"({ErrorCodes.NotFound})", TextOf(result), StringComparison.Ordinal);
        }

        [Fact]
        public async Task CallTool_AddOnlyKeyOverwriting_IsRefusedByTheApi()
        {
            using StringContent content = new StringContent("kept", Encoding.UTF8, "text/plain");
            (await _host.AdminClient.PutAsync("/api/shelves/docs/content?path=/a.txt", content)).EnsureSuccessStatusCode();
            await using McpClient client = await ConnectAsync(await CreateKeyAsync("docs", ShelfKeyPermission.AddOnly));

            CallToolResult result = await client.CallToolAsync(
                "upload_file", new Dictionary<string, object?> { ["shelf"] = "docs", ["path"] = "/a.txt", ["text"] = "wiped", ["overwrite"] = true });

            Assert.Equal(true, result.IsError);
            Assert.EndsWith($"({ErrorCodes.PermissionDenied})", TextOf(result), StringComparison.Ordinal);
            Assert.Equal("kept", await _host.AdminClient.GetStringAsync("/api/shelves/docs/content?path=/a.txt"));
        }

        [Fact]
        public async Task CallTool_WithALocalPath_SaysTheServerCannotReachTheAgentsFiles()
        {
            await using McpClient client = await ConnectAsync(await CreateKeyAsync("docs", ShelfKeyPermission.ReadWrite));

            CallToolResult result = await client.CallToolAsync("upload_file", new Dictionary<string, object?> { ["shelf"] = "docs", ["path"] = "/a.txt", ["localPath"] = "/etc/hostname" });

            Assert.Equal(true, result.IsError);
            Assert.Contains("cannot reach your files", TextOf(result), StringComparison.Ordinal);
        }

        [Fact]
        public async Task CallTool_NotOfferedToTheKey_IsAnUnknownToolAndChangesNothing()
        {
            using StringContent content = new StringContent("kept", Encoding.UTF8, "text/plain");
            (await _host.AdminClient.PutAsync("/api/shelves/docs/content?path=/a.txt", content)).EnsureSuccessStatusCode();
            (await _host.AdminClient.DeleteAsync("/api/shelves/docs/files?path=/a.txt")).EnsureSuccessStatusCode();
            await using McpClient client = await ConnectAsync(await CreateKeyAsync("docs", ShelfKeyPermission.Read));

            McpProtocolException exception = await Assert.ThrowsAsync<McpProtocolException>(
                () => client.CallToolAsync("empty_trash", new Dictionary<string, object?> { ["shelf"] = "docs" }).AsTask());

            Assert.Contains("empty_trash", exception.Message, StringComparison.Ordinal);
            Assert.Single((await _host.AdminClient.GetFromJsonAsync<List<TrashEntry>>("/api/shelves/docs/trash"))!);
        }

        [Fact]
        public async Task Post_WithoutAKey_Returns401()
        {
            HttpResponseMessage response = await _host.AnonymousClient.PostAsync("/mcp", JsonContent.Create(new { jsonrpc = "2.0", id = 1, method = "tools/list" }));

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Post_WithAUsersSession_Returns401()
        {
            HttpResponseMessage response = await _host.AdminClient.PostAsync("/mcp", JsonContent.Create(new { jsonrpc = "2.0", id = 1, method = "tools/list" }));

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Post_WithAKeyThatIsNotOne_Returns401()
        {
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method = "tools/list" }) };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ShelfKeys.SecretPrefix + "not-a-key");

            HttpResponseMessage response = await _host.AnonymousClient.SendAsync(request);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task LlmsTxt_WithoutSigningIn_DescribesTheServerAsText()
        {
            HttpResponseMessage response = await _host.AnonymousClient.GetAsync("/llms.txt");

            Assert.Equal(("text/plain", HttpStatusCode.OK), (response.Content.Headers.ContentType?.MediaType, response.StatusCode));
            Assert.StartsWith("# ByteShelf", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        private static string TextOf(CallToolResult result)
        {
            return string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));
        }

        private static void AssertSucceeded(CallToolResult result)
        {
            Assert.True(result.IsError != true, $"The tool failed: {TextOf(result)}");
        }

        private async Task<string> CreateKeyAsync(string shelf, ShelfKeyPermission permission)
        {
            return (await _host.CreateKeyAsync(shelf, permission)).Secret;
        }

        private Task<McpClient> ConnectAsync(string secret, bool asBearer = true, Uri? endpoint = null, ServerTestHost? host = null)
        {
            HttpClientTransportOptions options = new HttpClientTransportOptions
            {
                Endpoint = endpoint ?? new Uri("http://localhost/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
                AdditionalHeaders = asBearer
                    ? new Dictionary<string, string> { ["Authorization"] = $"Bearer {secret}" }
                    : new Dictionary<string, string> { [ShelfKeys.HeaderName] = secret },
            };

            return McpClient.CreateAsync(new HttpClientTransport(options, (host ?? _host).CreateClient(), ownsHttpClient: true));
        }
    }
}
