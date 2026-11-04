using System.Net;
using ByteShelf.Server.Shared.StaticFiles;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;

namespace ByteShelf.Server.Tests.Shared
{
    /// <summary>How the published web UI is served: hashed bundles cached for good, everything else revalidated, deep links to the page.</summary>
    public sealed class StaticFileCachePolicyTests : IAsyncLifetime
    {
        private const string IndexHtml = "<html>the web UI</html>";

        private readonly string _webRoot = Path.Combine(Path.GetTempPath(), "byteshelf-webroot-" + Guid.NewGuid().ToString("N"));
        private WebApplication _app = null!;
        private HttpClient _client = null!;

        public async Task InitializeAsync()
        {
            Directory.CreateDirectory(Path.Combine(_webRoot, "assets"));
            await File.WriteAllTextAsync(Path.Combine(_webRoot, "index.html"), IndexHtml);
            await File.WriteAllTextAsync(Path.Combine(_webRoot, "assets", "index-abc.js"), "console.log('bundle');");
            await File.WriteAllTextAsync(Path.Combine(_webRoot, "manifest.json"), "{}");

            // The same static-file setup Program.BuildApplication uses, on a web root of the test's own.
            WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { WebRootPath = _webRoot });
            builder.WebHost.UseTestServer();
            _app = builder.Build();
            _app.UseDefaultFiles();
            _app.UseStaticFiles(new StaticFileOptions { OnPrepareResponse = StaticFileCachePolicy.Apply });
            _app.UseRouting();
            StaticFileCachePolicy.MapSpaFallback(_app);
            await _app.StartAsync();
            _client = _app.GetTestClient();
        }

        public async Task DisposeAsync()
        {
            _client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
            Directory.Delete(_webRoot, recursive: true);
        }

        [Fact]
        public async Task Bundle_UnderAssets_IsCachedForGood()
        {
            HttpResponseMessage response = await _client.GetAsync("/assets/index-abc.js");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("public, max-age=31536000, immutable", response.Headers.CacheControl?.ToString());
        }

        [Theory]
        [InlineData("/")]
        [InlineData("/index.html")]
        [InlineData("/manifest.json")]
        public async Task FileWithAStableUrl_IsRevalidatedEveryTime(string path)
        {
            HttpResponseMessage response = await _client.GetAsync(path);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            AssertNotCached(response);
        }

        [Fact]
        public async Task DeepLinkIntoTheWebUi_AnswersThePageUncached()
        {
            HttpResponseMessage response = await _client.GetAsync("/shelves/docs/notes");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(IndexHtml, await response.Content.ReadAsStringAsync());
            AssertNotCached(response);
        }

        [Fact]
        public async Task UnknownApiRoute_Answers404RatherThanThePage()
        {
            HttpResponseMessage response = await _client.GetAsync("/api/no-such-route");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task UnmatchedPost_Answers404RatherThanThePage()
        {
            HttpResponseMessage response = await _client.PostAsync("/shelves/docs", null);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        private static void AssertNotCached(HttpResponseMessage response)
        {
            Assert.True(response.Headers.CacheControl?.NoCache);
            Assert.True(response.Headers.CacheControl?.NoStore);
            Assert.True(response.Headers.CacheControl?.MustRevalidate);
        }
    }
}
