using System.Net.Http.Json;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelf.Server.Version.Models;
using ByteShelf.Server.Version.Services;
using Microsoft.AspNetCore.Hosting;
using Moq;

namespace ByteShelf.Server.Tests.Version
{
    /// <summary>The id the web UI polls to notice a new deploy.</summary>
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
        public async Task GetCurrent_OverHttp_NeedsNoSignIn()
        {
            await using ServerTestHost host = await ServerTestHost.StartAsync();

            VersionView? version = await host.AnonymousClient.GetFromJsonAsync<VersionView>("/api/version/current");

            Assert.Matches("^[0-9a-f]{32}$", version?.VersionId);
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

        private static VersionService ServiceOn(string? webRootPath)
        {
            return new VersionService(Mock.Of<IWebHostEnvironment>(environment => environment.WebRootPath == webRootPath));
        }
    }
}
