using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfClient;
using ByteShelfClient.Http;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.Client
{
    /// <summary>The client contract against the whole server, over HTTP, signed in as its admin.</summary>
    public class HttpClientContractTests : ClientContractTests
    {
        private ServerTestHost _host = null!;
        private HttpClient _http = null!;
        private UserSession _session = null!;

        public override async Task InitializeAsync()
        {
            _host = await ServerTestHost.StartAsync();
            await base.InitializeAsync();
        }

        public override async Task DisposeAsync()
        {
            _session.Dispose();
            _http.Dispose();
            await _host.DisposeAsync();
            await base.DisposeAsync();
        }

        [Fact]
        public async Task CreateDownloadLinkAsync_Link_ServesTheFileWithoutSigningIn()
        {
            await UploadTextAsync("/a.txt", "shared");

            DownloadLink link = await Shelf.CreateDownloadLinkAsync("/a.txt");

            Assert.StartsWith(_http.BaseAddress!.AbsoluteUri, link.Url, StringComparison.Ordinal);
            Assert.Equal("shared", await _host.AnonymousClient.GetStringAsync(new Uri(link.Url)));
        }

        protected override async Task<IByteShelfClient> CreateAdminClientAsync()
        {
            _http = _host.CreateClient();
            _session = await UserSession.SignInAsync(_http, ServerTestHost.AdminEmail, AdminPassword);

            // Small limits, so the longer test files go as resumable uploads in several chunks.
            return new ByteShelfHttpClient(_http, _session, new ByteShelfHttpClientOptions { SingleRequestMaxBytes = 16, ChunkBytes = 7 });
        }
    }
}
