using System.Net;
using System.Net.Http.Json;
using ByteShelf.Server.Plugins.Models;
using ByteShelf.Server.Plugins.Services;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelf.Server.Version.Models;
using ByteShelf.Server.Version.Services;
using ByteShelfCommon;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace ByteShelf.Server.Tests.Version
{
    /// <summary>The id the web UI polls to notice a new deploy, and the release only signed-in callers may read.</summary>
    public sealed class VersionServiceTests : IDisposable
    {
        private readonly string _webRoot = Path.Combine(Path.GetTempPath(), "byteshelf-webroot-" + Guid.NewGuid().ToString("N"));

        public VersionServiceTests()
        {
            Directory.CreateDirectory(_webRoot);
        }

        [Fact]
        public void GetCurrent_SameDeploy_IsTheSameEveryTime()
        {
            VersionService service = ServiceOn(WithIndex("<script src=\"/assets/index-abc.js\"></script>"));

            Assert.Equal(service.GetCurrent().VersionId, service.GetCurrent().VersionId);
            Assert.Equal(service.GetCurrent().VersionId, ServiceOn(_webRoot).GetCurrent().VersionId);
        }

        [Fact]
        public void GetCurrent_AnotherWebUi_IsAnotherId()
        {
            string before = ServiceOn(WithIndex("<script src=\"/assets/index-abc.js\"></script>")).GetCurrent().VersionId;

            string after = ServiceOn(WithIndex("<script src=\"/assets/index-def.js\"></script>")).GetCurrent().VersionId;

            Assert.NotEqual(before, after);
        }

        [Fact]
        public void GetCurrent_NoWebUiPublished_StillAnswersAnId()
        {
            string id = ServiceOn(null).GetCurrent().VersionId;

            Assert.Matches("^[0-9a-f]{32}$", id);
            Assert.NotEqual(id, ServiceOn(WithIndex("anything")).GetCurrent().VersionId);
        }

        [Fact]
        public void GetCurrent_AnotherPluginUi_IsAnotherId()
        {
            string none = ServiceOn(_webRoot).GetCurrent().VersionId;
            string before = ServiceOn(_webRoot, WithPluginUi("abc")).GetCurrent().VersionId;

            Assert.NotEqual(none, before);
            Assert.Equal(before, ServiceOn(_webRoot, WithPluginUi("abc")).GetCurrent().VersionId);
            Assert.NotEqual(before, ServiceOn(_webRoot, WithPluginUi("def")).GetCurrent().VersionId);
            Assert.NotEqual(before, ServiceOn(_webRoot, WithPluginUi("abc", "another")).GetCurrent().VersionId);
        }

        [Fact]
        public async Task GetCurrent_OverHttp_NeedsNoSignIn()
        {
            await using ServerTestHost host = await ServerTestHost.StartAsync();

            VersionView? version = await host.AnonymousClient.GetFromJsonAsync<VersionView>("/api/version/current");

            Assert.Matches("^[0-9a-f]{32}$", version?.VersionId);
        }

        [Fact]
        public void GetRelease_IsTheServersVersionWithoutTheCommitAndTheDeployId()
        {
            VersionService service = ServiceOn(WithIndex("anything"));

            ServerVersion release = service.GetRelease();

            // The assembly version carries the csproj's Version and never the commit.
            Assert.Equal(typeof(VersionService).Assembly.GetName().Version!.ToString(3), release.Version);
            Assert.Equal(service.GetCurrent().VersionId, release.VersionId);
        }

        [Fact]
        public async Task GetRelease_OverHttpSignedOut_IsRefused()
        {
            await using ServerTestHost host = await ServerTestHost.StartAsync();

            HttpResponseMessage response = await host.AnonymousClient.GetAsync("/api/version/release");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.DoesNotContain(ReleaseVersion.Of(typeof(VersionService).Assembly), await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        [Fact]
        public async Task GetRelease_OverHttpWithAKeyThatIsNoKey_IsRefused()
        {
            await using ServerTestHost host = await ServerTestHost.StartAsync();
            using HttpClient client = host.CreateKeyClient(ShelfKeys.SecretPrefix + "not-a-key");

            HttpResponseMessage response = await client.GetAsync("/api/version/release");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task GetRelease_OverHttpAsAUserWithoutRolesOrShelves_AnswersIt()
        {
            await using ServerTestHost host = await ServerTestHost.StartAsync();

            ServerVersion? release = await host.MemberClient.GetFromJsonAsync<ServerVersion>("/api/version/release");

            Assert.Equal(host.Services.GetRequiredService<IVersionService>().GetRelease().Version, release?.Version);
            Assert.Equal((await host.AnonymousClient.GetFromJsonAsync<VersionView>("/api/version/current"))?.VersionId, release?.VersionId);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task GetRelease_OverHttpWithAReadKey_AnswersIt(bool asBearer)
        {
            await using ServerTestHost host = await ServerTestHost.StartAsync();
            await host.CreateShelfAsync("docs");
            string secret = (await host.CreateKeyAsync("docs", ShelfKeyPermission.Read)).Secret;
            using HttpClient client = asBearer ? host.CreateClient(secret) : host.CreateKeyClient(secret);

            ServerVersion? release = await client.GetFromJsonAsync<ServerVersion>("/api/version/release");

            Assert.Equal(host.Services.GetRequiredService<IVersionService>().GetRelease().Version, release?.Version);
        }

        public void Dispose()
        {
            Directory.Delete(_webRoot, recursive: true);
        }

        private string WithIndex(string contents)
        {
            File.WriteAllText(Path.Combine(_webRoot, "index.html"), contents);
            return _webRoot;
        }

        private static PluginCatalog WithPluginUi(string hash, string id = "test")
        {
            PluginManifest manifest = new PluginManifest(new PluginDeclaration(id, "Test"), "1.0.0", null);
            return new PluginCatalog([new LoadedPlugin(manifest, new PluginUi(id, "/plugins/test/ui", hash, false), null)], []);
        }

        private static VersionService ServiceOn(string? webRootPath, IPluginCatalog? plugins = null)
        {
            return new VersionService(Mock.Of<IWebHostEnvironment>(environment => environment.WebRootPath == webRootPath), plugins ?? PluginCatalog.Empty);
        }
    }
}
