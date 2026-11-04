using System.IO.Compression;
using System.Net.Http.Json;
using System.Text;
using ByteShelf.Server.Backup.Services;
using ByteShelf.Server.Email;
using ByteShelf.Server.Shared.Root;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace ByteShelf.Server.Tests.Backup
{
    /// <summary>A backup unzipped is a root a server starts from, keeping its sign-ins and files.</summary>
    public sealed class RestoredBackupTests : IAsyncLifetime
    {
        private ServerTestHost _host = null!;
        private string _zipPath = null!;
        private string _restored = null!;

        public async Task InitializeAsync()
        {
            _host = await ServerTestHost.StartAsync();
            _zipPath = _host.TestRoot.Root.Path + "-backup.zip";
            _restored = _host.TestRoot.Root.Path + "-restored";
        }

        public async Task DisposeAsync()
        {
            await _host.DisposeAsync();
            SqliteConnection.ClearAllPools();
            File.Delete(_zipPath);
            if (Directory.Exists(_restored))
            {
                Directory.Delete(_restored, recursive: true);
            }
        }

        [Fact]
        public async Task Restored_ServerStartsOnIt_WithTheSameSignInsAndFiles()
        {
            (await _host.AdminClient.PostAsJsonAsync("/api/shelves", new CreateShelfRequest { Name = "docs" })).EnsureSuccessStatusCode();
            using StringContent content = new StringContent("kept", Encoding.UTF8, "text/plain");
            (await _host.AdminClient.PutAsync("/api/shelves/docs/content?path=/a.txt", content)).EnsureSuccessStatusCode();

            await RootBackup.WriteAsync(_host.TestRoot.Root, _zipPath, CancellationToken.None);
            ZipFile.ExtractToDirectory(_zipPath, _restored);

            await using WebApplication restored = Program.BuildApplication(
                new[] { "--environment", "Development" },
                RootFolder.OpenExisting(_restored),
                builder =>
                {
                    builder.WebHost.UseTestServer();
                    builder.Logging.SetMinimumLevel(LogLevel.Warning);
                },
                EmailOptions.None);
            await restored.StartAsync();
            using HttpClient client = restored.GetTestClient();

            // The original server's sign-in: keys/ came along, so the restored server accepts it.
            client.DefaultRequestHeaders.Authorization = _host.AdminClient.DefaultRequestHeaders.Authorization;
            Assert.Equal("kept", await client.GetStringAsync("/api/shelves/docs/content?path=/a.txt"));
            await restored.StopAsync();
        }
    }
}
