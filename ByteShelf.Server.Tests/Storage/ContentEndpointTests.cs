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
    /// the headers on downloaded content, sign-in and roles, and the status codes and bodies the
    /// middleware writes.
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
        public async Task Get_SignedInWithoutAdmin_Returns403()
        {
            HttpResponseMessage response = await _host.MemberClient.GetAsync("/api/shelves/docs/entries");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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
