using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using ByteShelf.Tools;
using ByteShelf.Tools.Mcp;
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
            "download_deleted", "download_file", "find_duplicates", "get_info", "list_directory", "list_links", "list_shelves", "list_trash", "list_tree", "revoke_link", "search_files", "share_file", "shelf_info",
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
        public async Task ListTools_KeyReadingOneShelfAndAddingToAnother_OffersWhatEitherAllows()
        {
            await using McpClient client = await ConnectAsync(await CreateKeyOnBothAsync(ShelfKeyPermission.Read, ShelfKeyPermission.AddOnly));

            IList<McpClientTool> tools = await client.ListToolsAsync();

            Assert.Equal(ReadTools.Concat(AddTools).Order(StringComparer.Ordinal), tools.Select(tool => tool.Name).Order(StringComparer.Ordinal));
        }

        [Fact]
        public async Task Connect_KeyOnTwoShelves_InstructionsNameEachWithItsAccessAndStoreOnlyWhereItMay()
        {
            await using McpClient client = await ConnectAsync(await CreateKeyOnBothAsync(ShelfKeyPermission.Read, ShelfKeyPermission.AddOnly));
            string instructions = client.ServerInstructions!;

            Assert.Contains("give every tool the shelf to work on", instructions, StringComparison.Ordinal);
            Assert.Contains("- \"docs\": read", instructions, StringComparison.Ordinal);
            Assert.Contains("- \"other\": add-only", instructions, StringComparison.Ordinal);
            Assert.DoesNotContain("when you leave shelf out", Regex.Replace(instructions, @"\s+", " "), StringComparison.Ordinal);
            Assert.Contains("-T ./q3.pdf \"http://localhost/api/shelves/other/content", instructions, StringComparison.Ordinal);
            Assert.DoesNotContain("-T ./q3.pdf \"http://localhost/api/shelves/docs/content", instructions, StringComparison.Ordinal);
            Assert.Contains("never over a file already there", instructions, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Connect_KeyReadingTwoShelves_InstructionsShowNoStoring()
        {
            await using McpClient client = await ConnectAsync(await CreateKeyOnBothAsync(ShelfKeyPermission.Read, ShelfKeyPermission.Read));

            Assert.DoesNotContain("-T ./q3.pdf", client.ServerInstructions, StringComparison.Ordinal);
        }

        [Fact]
        public async Task CallTool_KeyOnTwoShelves_IsRefusedOnTheShelfThatDoesNotAllowIt()
        {
            await using McpClient client = await ConnectAsync(await CreateKeyOnBothAsync(ShelfKeyPermission.Read, ShelfKeyPermission.AddOnly));

            CallToolResult onOther = await client.CallToolAsync("upload_file", new Dictionary<string, object?> { ["shelf"] = "other", ["path"] = "/a.txt", ["text"] = "hello" });
            CallToolResult onDocs = await client.CallToolAsync("upload_file", new Dictionary<string, object?> { ["shelf"] = "docs", ["path"] = "/a.txt", ["text"] = "hello" });

            AssertSucceeded(onOther);
            Assert.True(onDocs.IsError == true);
            Assert.Contains("(permission_denied)", TextOf(onDocs), StringComparison.Ordinal);
        }

        [Fact]
        public async Task CallTool_ListShelvesWithAKeyOnTwoShelves_ListsExactlyThose()
        {
            await _host.CreateShelfAsync("hidden");
            await using McpClient client = await ConnectAsync(await CreateKeyOnBothAsync(ShelfKeyPermission.Read, ShelfKeyPermission.AddOnly));

            CallToolResult listed = await client.CallToolAsync("list_shelves", new Dictionary<string, object?>());

            AssertSucceeded(listed);
            Assert.Equal(new[] { "docs", "other" }, TextOf(listed).Split(Environment.NewLine).Select(line => line.Split("  ")[0]));
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
            await TestReplicas.MarkAsReplicaAsync(_host.Services);

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
        public async Task GetInfoAndSearchFiles_ReadPdf_ShowAndFindWhatItsContentsSay()
        {
            await StoreAsync("/q3.pdf", TestMedia.Pdf(pages: 3, title: "Q3 report"), "application/pdf");
            await _host.Services.ReadMetadataAsync();

            await using McpClient client = await ConnectAsync(await CreateKeyAsync("docs", ShelfKeyPermission.Read), asBearer: true);
            string info = TextOf(await client.CallToolAsync("get_info", new Dictionary<string, object?> { ["path"] = "/q3.pdf" }));
            string found = TextOf(await client.CallToolAsync("search_files", new Dictionary<string, object?> { ["text"] = "q3 report", ["minPageCount"] = 3 }));

            Assert.Contains("pages        3", info, StringComparison.Ordinal);
            Assert.Contains("title        Q3 report", info, StringComparison.Ordinal);
            Assert.StartsWith("/q3.pdf", found, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("search_files")]
        [InlineData("find_duplicates")]
        public async Task SearchTool_EveryMetadataCriterion_IsAskedOfTheServer(string tool)
        {
            // The matching file, then one per criterion that fails only that one, each stored twice so
            // find_duplicates groups it. Built once each: two builds of a PDF differ in their bytes.
            DateTimeOffset taken = new DateTimeOffset(2024, 6, 1, 9, 0, 0, TimeSpan.Zero);
            const string Gps = "+59.3293+018.0686/";
            Dictionary<string, byte[]> files = new Dictionary<string, byte[]>
            {
                ["trip.mov"] = TestMedia.Movie(taken, TimeSpan.FromSeconds(90), location: Gps),
                ["indoors.mov"] = TestMedia.Movie(taken, TimeSpan.FromSeconds(90)),
                ["early.mov"] = TestMedia.Movie(new DateTimeOffset(2024, 5, 31, 23, 0, 0, TimeSpan.Zero), TimeSpan.FromSeconds(90), location: Gps),
                ["late.mov"] = TestMedia.Movie(new DateTimeOffset(2024, 6, 2, 0, 0, 0, TimeSpan.Zero), TimeSpan.FromSeconds(90), location: Gps),
                ["short.mov"] = TestMedia.Movie(taken, TimeSpan.FromSeconds(30), location: Gps),
                ["long.mov"] = TestMedia.Movie(taken, TimeSpan.FromSeconds(150), location: Gps),
                ["q3.pdf"] = TestMedia.Pdf(pages: 3, title: "Q3 report"),
                ["memo.pdf"] = TestMedia.Pdf(pages: 1, title: "Memo"),
                ["thesis.pdf"] = TestMedia.Pdf(pages: 4, title: "Thesis"),
            };
            foreach (string copy in new[] { "a", "b" })
            {
                foreach ((string name, byte[] content) in files)
                {
                    await StoreAsync($"/{copy}/{name}", content, name.EndsWith(".pdf", StringComparison.Ordinal) ? "application/pdf" : "video/quicktime");
                }
            }

            await _host.Services.ReadMetadataAsync();
            await using McpClient client = await ConnectAsync(await CreateKeyAsync("docs", ShelfKeyPermission.Read), asBearer: true);

            string videos = TextOf(await client.CallToolAsync(tool, new Dictionary<string, object?>
            {
                ["takenFrom"] = "2024-06-01T00:00:00Z",
                ["takenBefore"] = "2024-06-02T00:00:00Z",
                ["hasGps"] = true,
                ["minDurationSeconds"] = 60,
                ["maxDurationSeconds"] = 120,
            }));
            string documents = TextOf(await client.CallToolAsync(tool, new Dictionary<string, object?> { ["minPageCount"] = 2, ["maxPageCount"] = 3 }));

            Assert.Equal(["/a/trip.mov", "/b/trip.mov"], PathsIn(videos));
            Assert.Equal(["/a/q3.pdf", "/b/q3.pdf"], PathsIn(documents));
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

        /// <summary>No link reaches the trash, so a deleted file not answered here points to the CLI's get-deleted.</summary>
        [Fact]
        public async Task CallTool_DownloadDeletedBinaryFile_PointsToGetDeleted()
        {
            await using McpClient client = await ConnectAsync(await CreateKeyAsync("docs", ShelfKeyPermission.ReadWrite));
            AssertSucceeded(await client.CallToolAsync("upload_file", new Dictionary<string, object?> { ["path"] = "/a.bin", ["base64"] = Convert.ToBase64String(new byte[] { 0xff, 0xfe, 0x00 }) }));
            TrashEntry deleted = (await (await _host.AdminClient.DeleteAsync("/api/shelves/docs/files?path=/a.bin")).Content.ReadFromJsonAsync<TrashEntry>())!;

            CallToolResult read = await client.CallToolAsync("download_deleted", new Dictionary<string, object?> { ["deletionId"] = deleted.DeletionId });

            Assert.True(read.IsError);
            Assert.Contains($"byteshelf get-deleted docs {deleted.DeletionId:D}", TextOf(read), StringComparison.Ordinal);
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
        public async Task CallTool_DownloadALargeImage_AnswersAPreviewAndHowToFetchItWhole()
        {
            byte[] png = TestImages.PhotoLikePng(2400, 1600);
            Assert.True(png.Length > ByteShelfMcpTools.MaxInlineImageBytes);
            await StoreAsync("/a.png", png, "image/png");
            await using McpClient client = await ConnectAsync(await CreateKeyAsync("docs", ShelfKeyPermission.Read));

            CallToolResult read = await client.CallToolAsync("download_file", new Dictionary<string, object?> { ["path"] = "/a.png" });

            AssertSucceeded(read);
            ImageContentBlock image = Assert.Single(read.Content.OfType<ImageContentBlock>());
            Assert.Equal("image/jpeg", image.MimeType);
            int height = (int)Math.Round(1600.0 * ImagePreview.LongSide / 2400);
            Assert.Equal((ImagePreview.LongSide, height), TestImages.SizeOf(image.DecodedData.ToArray()));
            Assert.Equal(
                $"/a.png is a 2400×1600 image/png of {TextFormat.Size(png.Length)}; this is a {ImagePreview.LongSide}×{height} JPEG preview of it. "
                + "For the whole file, make a link to it with share_file and fetch it from there, such as with curl.",
                TextOf(read));
        }

        [Fact]
        public async Task CallTool_DownloadASmallImageOfAKindModelsCannotTake_AnswersAPreview()
        {
            await StoreAsync("/scan.bmp", TestImages.Bmp(300, 200), "image/bmp");
            await using McpClient client = await ConnectAsync(await CreateKeyAsync("docs", ShelfKeyPermission.Read));

            CallToolResult read = await client.CallToolAsync("download_file", new Dictionary<string, object?> { ["path"] = "/scan.bmp" });

            AssertSucceeded(read);
            Assert.Equal((300, 200), TestImages.SizeOf(Assert.Single(read.Content.OfType<ImageContentBlock>()).DecodedData.ToArray()));
            Assert.StartsWith("/scan.bmp is a 300×200 image/bmp of", TextOf(read), StringComparison.Ordinal);
        }

        [Fact]
        public async Task CallTool_DownloadTextStoredAsAnImage_AnswersTheText()
        {
            const string svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\"/>";
            await StoreAsync("/logo.svg", Encoding.UTF8.GetBytes(svg), "image/svg+xml");
            await using McpClient client = await ConnectAsync(await CreateKeyAsync("docs", ShelfKeyPermission.Read));

            CallToolResult read = await client.CallToolAsync("download_file", new Dictionary<string, object?> { ["path"] = "/logo.svg" });

            AssertSucceeded(read);
            Assert.Empty(read.Content.OfType<ImageContentBlock>());
            Assert.Equal(svg, TextOf(read));
        }

        [Fact]
        public async Task CallTool_DownloadAnImageItCannotDecode_AnswersOnlyHowToFetchItWhole()
        {
            await StoreAsync("/a.heic", TestImages.HeicHeaderOnly(ByteShelfMcpTools.MaxInlineImageBytes + 1), "image/heic");
            await using McpClient client = await ConnectAsync(await CreateKeyAsync("docs", ShelfKeyPermission.Read));

            CallToolResult read = await client.CallToolAsync("download_file", new Dictionary<string, object?> { ["path"] = "/a.heic" });

            Assert.Equal(true, read.IsError);
            Assert.EndsWith("/a.heic (image/heic) is not an image these tools can make a preview of; make a link to it with share_file and fetch it from there, such as with curl.", TextOf(read), StringComparison.Ordinal);
        }

        [Fact]
        public async Task CallTool_DownloadAnImageDecodingPastTheMemoryBudget_SaysSoAndHowToFetchItWhole()
        {
            byte[] png = TestImages.BlankRgbaPng(9000, 9000);
            Assert.True(png.Length > ByteShelfMcpTools.MaxInlineImageBytes);
            await StoreAsync("/huge.png", png, "image/png");
            await using McpClient client = await ConnectAsync(await CreateKeyAsync("docs", ShelfKeyPermission.Read));

            CallToolResult read = await client.CallToolAsync("download_file", new Dictionary<string, object?> { ["path"] = "/huge.png" });

            Assert.Equal(true, read.IsError);
            Assert.EndsWith("/huge.png decodes to more pixels than a preview is made from here; make a link to it with share_file and fetch it from there, such as with curl.", TextOf(read), StringComparison.Ordinal);
        }

        [Fact]
        public async Task CallTool_DownloadAnImageOverThePreviewCap_IsRefusedUnread()
        {
            await StoreAsync("/a.png", new byte[ImagePreview.MaxSourceBytes + 1], "image/png");
            await using McpClient client = await ConnectAsync(await CreateKeyAsync("docs", ShelfKeyPermission.Read));

            CallToolResult read = await client.CallToolAsync("download_file", new Dictionary<string, object?> { ["path"] = "/a.png" });

            Assert.Equal(true, read.IsError);
            Assert.Contains(
                $"/a.png is {TextFormat.Size(ImagePreview.MaxSourceBytes + 1)}, more than the {TextFormat.Size(ImagePreview.MaxSourceBytes)} answered here as an image or a preview of one;",
                TextOf(read),
                StringComparison.Ordinal);
        }

        [Fact]
        public async Task CallTool_MissingARequiredArgument_NamesIt()
        {
            await using McpClient client = await ConnectAsync(await CreateKeyAsync("docs", ShelfKeyPermission.ReadWrite));

            CallToolResult shared = await client.CallToolAsync("share_file", new Dictionary<string, object?> { ["expiresIn"] = "1h" });

            Assert.Equal(true, shared.IsError);
            Assert.Equal("share_file needs path.", TextOf(shared));
        }

        [Fact]
        public async Task CallTool_MoveAndSetDescriptionWithLists_ApplyEachItem()
        {
            await using McpClient client = await ConnectAsync(await CreateKeyAsync("docs", ShelfKeyPermission.ReadWrite));
            AssertSucceeded(await client.CallToolAsync("upload_file", new Dictionary<string, object?> { ["path"] = "/a.txt", ["text"] = "a" }));
            AssertSucceeded(await client.CallToolAsync("upload_file", new Dictionary<string, object?> { ["path"] = "/b.txt", ["text"] = "b" }));

            CallToolResult moved = await client.CallToolAsync("move", new Dictionary<string, object?>
            {
                ["moves"] = new[] { new Dictionary<string, string> { ["path"] = "/a.txt", ["toPath"] = "/archive/a.txt" } },
            });
            CallToolResult intoDirectory = await client.CallToolAsync("move", new Dictionary<string, object?> { ["paths"] = new[] { "/b.txt" }, ["intoDirectory"] = "/archive" });
            CallToolResult described = await client.CallToolAsync("set_description", new Dictionary<string, object?>
            {
                ["descriptions"] = new[] { new Dictionary<string, string> { ["path"] = "/archive/a.txt", ["description"] = "Signed" } },
            });

            Assert.Equal("Moved /a.txt to /archive/a.txt", TextOf(moved));
            Assert.Equal("Moved /b.txt to /archive/b.txt", TextOf(intoDirectory));
            Assert.Equal("Described /archive/a.txt", TextOf(described));
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
        public async Task CallTool_ReadKeySharesListsAndRevokesALink_LinkServesTheFileThenNoLonger()
        {
            (await _host.AdminClient.PutAsync("/api/shelves/docs/content?path=/a.txt", new StringContent("once"))).EnsureSuccessStatusCode();
            await using McpClient client = await ConnectAsync(await CreateKeyAsync("docs", ShelfKeyPermission.Read));

            CallToolResult shared = await client.CallToolAsync("share_file", new Dictionary<string, object?> { ["path"] = "/a.txt", ["expiresIn"] = "5m" });
            string url = new Uri(TextOf(shared).Split(Environment.NewLine)[0]).PathAndQuery;
            string linkId = Regex.Match(TextOf(shared), "its id is ([0-9a-f-]{36})").Groups[1].Value;
            string read = await _host.AnonymousClient.GetStringAsync(url);
            string listed = TextOf(await client.CallToolAsync("list_links", new Dictionary<string, object?>()));
            CallToolResult revoked = await client.CallToolAsync("revoke_link", new Dictionary<string, object?> { ["linkId"] = linkId });

            Assert.Equal("once", read);
            Assert.StartsWith($"{linkId}  reads /a.txt  by ", listed, StringComparison.Ordinal);
            AssertSucceeded(revoked);
            Assert.Equal(HttpStatusCode.NotFound, (await _host.AnonymousClient.GetAsync(url)).StatusCode);
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
            await StoreAsync("/a.txt", Encoding.UTF8.GetBytes("kept"), "text/plain");
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
            await StoreAsync("/a.txt", Encoding.UTF8.GetBytes("kept"), "text/plain");
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

        /// <summary>Stores <paramref name="content"/> on the docs shelf as the admin, typed <paramref name="contentType"/>.</summary>
        /// <summary>The stored files' paths a tool's answer names, in order.</summary>
        private static List<string> PathsIn(string answer)
        {
            return Regex.Matches(answer, @"/[ab]/[\w.]+").Select(match => match.Value).Order(StringComparer.Ordinal).ToList();
        }

        private async Task StoreAsync(string path, byte[] content, string contentType)
        {
            await _host.PutAsAdminAsync("docs", path, content, contentType);
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

        /// <summary>A key made on "docs" with <paramref name="onDocs"/>, then given "other" with <paramref name="onOther"/>.</summary>
        private async Task<string> CreateKeyOnBothAsync(ShelfKeyPermission onDocs, ShelfKeyPermission onOther)
        {
            CreatedShelfKey created = await _host.CreateKeyAsync("docs", onDocs);
            (await _host.AdminClient.PutAsJsonAsync($"/api/keys/{created.Key.Id}/grants/other", new SetShelfKeyGrantRequest { Permission = onOther })).EnsureSuccessStatusCode();
            return created.Secret;
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
