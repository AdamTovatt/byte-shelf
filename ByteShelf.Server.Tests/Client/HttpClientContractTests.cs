using ByteShelf.Server.Replication.Services;
using ByteShelf.Server.Tests.Replication;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfClient;
using ByteShelfClient.Http;
using ByteShelfCommon;
using Microsoft.Extensions.DependencyInjection;

namespace ByteShelf.Server.Tests.Client
{
    /// <summary>The client contract against the whole server, over HTTP, signed in as its admin.</summary>
    public class HttpClientContractTests : ClientContractTests
    {
        private readonly List<ServerTestHost> _followers = new List<ServerTestHost>();
        private readonly List<UserSession> _followerSessions = new List<UserSession>();
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
            foreach (UserSession session in _followerSessions)
            {
                session.Dispose();
            }

            foreach (ServerTestHost follower in _followers)
            {
                await follower.DisposeAsync();
            }

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

        [Fact]
        public async Task CreateUploadLinkAsync_Link_StoresTheFileWithoutSigningIn()
        {
            UploadLink link = await Shelf.CreateUploadLinkAsync("/in/a.txt", new UploadLinkOptions { Description = "sent in" });

            Assert.StartsWith(_http.BaseAddress!.AbsoluteUri, link.Url, StringComparison.Ordinal);
            (await _host.AnonymousClient.PutAsync(new Uri(link.Url), new StringContent("received"))).EnsureSuccessStatusCode();
            ShelfFile stored = await Shelf.GetFileAsync("/in/a.txt");
            Assert.Equal(("received", "sent in"), (await ReadTextAsync("/in/a.txt"), stored.Description));
        }

        protected override async Task<IByteShelfClient> CreateAdminClientAsync()
        {
            _http = _host.CreateClient();
            _session = await UserSession.SignInAsync(_http, ServerTestHost.AdminEmail, AdminPassword);

            // Small limits, so the longer test files go as resumable uploads in several chunks.
            return new ByteShelfHttpClient(_http, _session, new ByteShelfHttpClientOptions { SingleRequestMaxBytes = 16, ChunkBytes = 7 });
        }

        protected override string PrimaryUrl => _host.Services.GetRequiredService<ReplicationOptions>().PublicUrl;

        protected override async Task<IByteShelfClient> CreateServerToFollowAsync()
        {
            ServerTestHost follower = await ReplicationPair.StartServerToFollowAsync(_host);
            _followers.Add(follower);
            HttpClient http = follower.CreateClient();
            UserSession session = await UserSession.SignInAsync(http, ServerTestHost.AdminEmail, AdminPassword);
            _followerSessions.Add(session);
            return new ByteShelfHttpClient(http, session);
        }
    }
}
