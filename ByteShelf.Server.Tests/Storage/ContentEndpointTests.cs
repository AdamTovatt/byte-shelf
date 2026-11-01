using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.Storage
{
    /// <summary>
    /// The HTTP behaviour only the pipeline shows: routing, streaming PUT, Range and ETag handling,
    /// the headers on downloaded content, sign-in, and the status codes and bodies the middleware
    /// writes. Who may do what is in <c>AccessEndpointTests</c>.
    /// </summary>
    public class ContentEndpointTests : IAsyncLifetime
    {
        private ServerTestHost _host = null!;

        public async Task InitializeAsync()
        {
            _host = await ServerTestHost.StartAsync();
            HttpResponseMessage created = await _host.AdminClient.PostAsJsonAsync("/api/shelves", new CreateShelfRequest { Name = "docs", QuotaBytes = 1000 });
            created.EnsureSuccessStatusCode();
        }

        public async Task DisposeAsync()
        {
            await _host.DisposeAsync();
        }

        [Fact]
        public async Task GetShelves_AfterCreate_ListsTheShelfWithItsQuota()
        {
            IReadOnlyList<ShelfInfo>? shelves = await _host.AdminClient.GetFromJsonAsync<IReadOnlyList<ShelfInfo>>("/api/shelves");

            ShelfInfo shelf = Assert.Single(shelves!);
            Assert.Equal("docs", shelf.Name);
            Assert.Equal(1000, shelf.QuotaBytes);
        }

        [Fact]
        public async Task Search_CriteriaInTheQueryString_BindByTheirCamelCaseNames()
        {
            await PutAsync("/reports/q3.txt", "abc", "text/plain");
            await PutAsync("/reports/q3.png", "abc", "image/png");

            FileSearchResults? found = await _host.AdminClient.GetFromJsonAsync<FileSearchResults>(
                "/api/shelves/docs/search?under=%2Freports&text=Q3&contentType=text%2Fplain&maxSizeBytes=3&limit=5");

            Assert.Equal("/reports/q3.txt", Assert.Single(found!.Files).Path);
        }

        [Fact]
        public async Task Tree_QueryInTheQueryString_BindsByCamelCaseNames()
        {
            await PutAsync("/reports/2026/q3.txt", "abc", "text/plain");
            await PutAsync("/reports/q2.txt", "abc", "text/plain");

            DirectoryTree? tree = await _host.AdminClient.GetFromJsonAsync<DirectoryTree>("/api/shelves/docs/tree?under=%2Freports&depth=1&limit=1");

            Assert.Equal(("/reports/2026", "/reports/2026"), (Assert.Single(tree!.Directories).Path, tree.NextCursor));
            Assert.Empty(tree.Files);
        }

        [Fact]
        public async Task Tree_DepthNotAllowed_Returns400()
        {
            HttpResponseMessage response = await _host.AdminClient.GetAsync("/api/shelves/docs/tree?depth=0");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(ErrorCodes.InvalidRequest, (await response.Content.ReadFromJsonAsync<ErrorResponse>())?.Code);
        }

        [Fact]
        public async Task Search_CriterionNotAllowed_Returns400()
        {
            HttpResponseMessage response = await _host.AdminClient.GetAsync("/api/shelves/docs/search?limit=0");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(ErrorCodes.InvalidRequest, (await response.Content.ReadFromJsonAsync<ErrorResponse>())?.Code);
        }

        [Fact]
        public async Task PutDescription_OverTheLimit_Returns400AndKeepsTheFileUndescribed()
        {
            await PutAsync("/a.txt", "a");

            HttpResponseMessage response = await _host.AdminClient.PutAsJsonAsync(
                "/api/shelves/docs/files/description?path=/a.txt", new SetDescriptionRequest { Description = new string('a', FileDescription.MaxLength + 1) });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(ErrorCodes.InvalidRequest, (await response.Content.ReadFromJsonAsync<ErrorResponse>())?.Code);
            Assert.Null((await _host.AdminClient.GetFromJsonAsync<ShelfFile>("/api/shelves/docs/files?path=/a.txt"))!.Description);
        }

        [Fact]
        public async Task PutThenGet_WithRange_ReturnsThePartAskedFor()
        {
            ShelfFile file = await PutAsync("/notes/hello.txt", "Hello, shelf!");

            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "/api/shelves/docs/content?path=/notes/hello.txt");
            request.Headers.Range = new RangeHeaderValue(7, 11);
            HttpResponseMessage response = await _host.AdminClient.SendAsync(request);

            Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
            Assert.Equal("shelf", await response.Content.ReadAsStringAsync());
            Assert.Equal($"\"{file.Sha256}\"", response.Headers.ETag?.Tag);
            Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        }

        [Fact]
        public async Task Get_UploadedHtml_IsServedSandboxedAndUnsniffed()
        {
            await PutAsync("/page.html", "<script>alert(1)</script>", "text/html");

            HttpResponseMessage response = await _host.AdminClient.GetAsync("/api/shelves/docs/content?path=/page.html");

            Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
            Assert.StartsWith("sandbox", Assert.Single(response.Headers.GetValues("Content-Security-Policy")), StringComparison.Ordinal);
        }

        /// <summary>
        /// Chrome's PDF viewer does not run under the sandbox, so a PDF goes without it; nosniff is
        /// what keeps a page labelled PDF from running as a page.
        /// </summary>
        [Theory]
        [InlineData("application/pdf")]
        [InlineData("Application/PDF")]
        public async Task Get_Pdf_IsServedUnsandboxedButUnsniffed(string contentType)
        {
            await PutAsync("/page.pdf", "<script>alert(1)</script>", contentType);

            HttpResponseMessage response = await _host.AdminClient.GetAsync("/api/shelves/docs/content?path=/page.pdf");

            Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType, ignoreCase: true);
            Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
            Assert.False(response.Headers.Contains("Content-Security-Policy"));
        }

        [Fact]
        public async Task Get_IfNoneMatchCurrentHash_ReturnsNotModified()
        {
            ShelfFile file = await PutAsync("/a.txt", "abc");

            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, $"/api/shelves/docs/content?id={file.Id}");
            request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue($"\"{file.Sha256}\""));
            HttpResponseMessage response = await _host.AdminClient.SendAsync(request);

            Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
        }

        [Fact]
        public async Task ResumableUpload_ChunksAtOffsets_StoresTheFile()
        {
            HttpResponseMessage started = await _host.AdminClient.PostAsJsonAsync(
                "/api/shelves/docs/uploads", new StartUploadRequest { Path = "/big.bin", SizeBytes = 6, ContentType = "application/x-test" });
            UploadSession upload = (await started.Content.ReadFromJsonAsync<UploadSession>())!;

            await SendChunkAsync(upload.Id, 0, "abc");
            HttpResponseMessage wrongOffset = await SendChunkAsync(upload.Id, 0, "def");
            await SendChunkAsync(upload.Id, 3, "def");
            HttpResponseMessage completed = await _host.AdminClient.PostAsync($"/api/shelves/docs/uploads/{upload.Id}/complete", null);

            Assert.Equal(HttpStatusCode.Conflict, wrongOffset.StatusCode);
            completed.EnsureSuccessStatusCode();
            Assert.Equal("abcdef", await _host.AdminClient.GetStringAsync("/api/shelves/docs/content?path=/big.bin"));
        }

        [Fact]
        public async Task PostDirectories_NewPath_ReturnsTheDirectory()
        {
            HttpResponseMessage response = await _host.AdminClient.PostAsJsonAsync("/api/shelves/docs/directories", new CreateDirectoryRequest { Path = "/archive/2026" });

            Assert.Equal("/archive/2026", (await response.Content.ReadFromJsonAsync<ShelfDirectory>())?.Path);
        }

        [Fact]
        public async Task PostFilesMove_ById_ReturnsTheFileAtItsNewPath()
        {
            ShelfFile file = await PutAsync("/inbox/a.txt", "a");

            HttpResponseMessage response = await _host.AdminClient.PostAsJsonAsync($"/api/shelves/docs/files/move?id={file.Id}", new MoveRequest { ToPath = "/archive/a.txt" });

            Assert.Equal("/archive/a.txt", (await response.Content.ReadFromJsonAsync<ShelfFile>())?.Path);
        }

        [Fact]
        public async Task PostDirectoriesMove_ByPath_ReturnsTheDirectoryAtItsNewPath()
        {
            await PutAsync("/inbox/a.txt", "a");

            HttpResponseMessage response = await _host.AdminClient.PostAsJsonAsync("/api/shelves/docs/directories/move?path=/inbox", new MoveRequest { ToPath = "/archive/inbox" });

            Assert.Equal("/archive/inbox", (await response.Content.ReadFromJsonAsync<ShelfDirectory>())?.Path);
        }

        [Fact]
        public async Task DeleteDirectories_ByPath_ReturnsTheDeletion()
        {
            await PutAsync("/inbox/a.txt", "a");

            HttpResponseMessage response = await _host.AdminClient.DeleteAsync("/api/shelves/docs/directories?path=/inbox");

            TrashEntry? deletion = await response.Content.ReadFromJsonAsync<TrashEntry>();
            Assert.Equal((TrashEntryKind.Directory, "/inbox"), (deletion?.Kind, deletion?.Path));
        }

        [Fact]
        public async Task PutShelf_NewName_ServesTheShelfUnderIt()
        {
            HttpResponseMessage response = await _host.AdminClient.PutAsJsonAsync("/api/shelves/docs", new UpdateShelfRequest { Name = "papers", QuotaBytes = 2000 });

            Assert.Equal(2000, (await response.Content.ReadFromJsonAsync<ShelfInfo>())?.QuotaBytes);
            Assert.Equal(HttpStatusCode.OK, (await _host.AdminClient.GetAsync("/api/shelves/papers/entries")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _host.AdminClient.GetAsync("/api/shelves/docs/entries")).StatusCode);
        }

        [Fact]
        public async Task DeleteShelf_OnlyTrashLeft_Returns409()
        {
            await PutAsync("/a.txt", "a");
            await _host.AdminClient.DeleteAsync("/api/shelves/docs/files?path=/a.txt");

            HttpResponseMessage response = await _host.AdminClient.DeleteAsync("/api/shelves/docs");

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }

        [Fact]
        public async Task DeleteTrash_ThenDeleteShelf_RemovesTheShelf()
        {
            await PutAsync("/a.txt", "a");
            await _host.AdminClient.DeleteAsync("/api/shelves/docs/files?path=/a.txt");

            HttpResponseMessage emptied = await _host.AdminClient.DeleteAsync("/api/shelves/docs/trash");
            HttpResponseMessage deleted = await _host.AdminClient.DeleteAsync("/api/shelves/docs");

            Assert.Equal((HttpStatusCode.NoContent, HttpStatusCode.NoContent), (emptied.StatusCode, deleted.StatusCode));
        }

        [Fact]
        public async Task Put_OverQuota_Returns507WithCode()
        {
            HttpResponseMessage response = await SendPutAsync("/big.bin", new string('x', 1001));

            Assert.Equal(HttpStatusCode.InsufficientStorage, response.StatusCode);
            Assert.Equal(ErrorCodes.QuotaExceeded, (await response.Content.ReadFromJsonAsync<ErrorResponse>())?.Code);
        }

        [Fact]
        public async Task Put_PathTaken_Returns409()
        {
            await PutAsync("/a.txt", "abc");

            HttpResponseMessage response = await SendPutAsync("/a.txt", "def");

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }

        [Fact]
        public async Task Put_WithoutContentLength_Returns411()
        {
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Put, "/api/shelves/docs/content?path=/a.txt")
            {
                Content = new StreamContent(new UnknownLengthStream(Encoding.UTF8.GetBytes("abc"))),
            };

            HttpResponseMessage response = await _host.AdminClient.SendAsync(request);

            Assert.Equal(HttpStatusCode.LengthRequired, response.StatusCode);
            Assert.Equal(ErrorCodes.InvalidRequest, (await response.Content.ReadFromJsonAsync<ErrorResponse>())?.Code);
        }

        [Fact]
        public async Task Get_UnknownShelf_Returns404()
        {
            HttpResponseMessage response = await _host.AdminClient.GetAsync("/api/shelves/nope/entries");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(ErrorCodes.NotFound, (await response.Content.ReadFromJsonAsync<ErrorResponse>())?.Code);
        }

        [Fact]
        public async Task Get_NotSignedIn_Returns401()
        {
            HttpResponseMessage response = await _host.AnonymousClient.GetAsync("/api/shelves/docs/entries");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Get_SignedInWithoutAccess_Returns404()
        {
            HttpResponseMessage response = await _host.MemberClient.GetAsync("/api/shelves/docs/entries");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task Get_UnknownApiRoute_Returns404NotThePage()
        {
            HttpResponseMessage response = await _host.AdminClient.PostAsync("/api/typo", null);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        private async Task<ShelfFile> PutAsync(string path, string text, string contentType = "text/plain")
        {
            HttpResponseMessage response = await SendPutAsync(path, text, contentType);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<ShelfFile>())!;
        }

        private Task<HttpResponseMessage> SendPutAsync(string path, string text, string contentType = "text/plain")
        {
            ByteArrayContent content = new ByteArrayContent(Encoding.UTF8.GetBytes(text));
            content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            return _host.AdminClient.PutAsync($"/api/shelves/docs/content?path={Uri.EscapeDataString(path)}", content);
        }

        private Task<HttpResponseMessage> SendChunkAsync(Guid uploadId, long offset, string text)
        {
            return _host.AdminClient.PutAsync($"/api/shelves/docs/uploads/{uploadId}?offset={offset}", new ByteArrayContent(Encoding.UTF8.GetBytes(text)));
        }

        /// <summary>A body whose length the client cannot know, so no Content-Length is sent.</summary>
        private sealed class UnknownLengthStream : MemoryStream
        {
            public UnknownLengthStream(byte[] bytes)
                : base(bytes)
            {
            }

            public override bool CanSeek => false;
        }
    }
}
