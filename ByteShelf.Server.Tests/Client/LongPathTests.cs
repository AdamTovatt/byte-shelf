using System.Text;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelf.Server.Users.Services;
using ByteShelfClient;
using ByteShelfClient.Http;
using ByteShelfCommon;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ByteShelf.Server.Tests.Client
{
    /// <summary>
    /// Paths as long as a shelf allows, over real HTTP: the in-memory test server has no request-line
    /// limit, so only Kestrel shows whether one fits. The names are non-ASCII, the longest a path gets
    /// once percent-encoded in a query string.
    /// </summary>
    public sealed class LongPathTests : IAsyncLifetime
    {
        private TestRoot _root = null!;
        private WebApplication _app = null!;
        private HttpClient _http = null!;
        private UserSession _session = null!;

        public async Task InitializeAsync()
        {
            _root = TestRoot.Create();
            _app = Program.BuildApplication(
                new[] { "--environment", "Development" },
                _root.Root,
                builder =>
                {
                    // Loopback only, on a port of the system's choosing.
                    builder.WebHost.UseUrls("http://127.0.0.1:0");
                    builder.Logging.SetMinimumLevel(LogLevel.Warning);
                });
            await _app.StartAsync();

            using (IServiceScope scope = _app.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<IUserService>().CreateUserAsync(ServerTestHost.AdminEmail, "Admin", ServerTestHost.Password, new[] { UserRoles.Admin });
            }

            _http = new HttpClient { BaseAddress = new Uri(_app.Urls.Single() + "/") };
            _session = await UserSession.SignInAsync(_http, ServerTestHost.AdminEmail, ServerTestHost.Password);
        }

        public async Task DisposeAsync()
        {
            _session.Dispose();
            _http.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
            await _root.DisposeAsync();
        }

        [Fact]
        public async Task LongestPath_OfThreeByteCharacters_IsStoredFoundAndSearchedPast()
        {
            IByteShelfClient client = new ByteShelfHttpClient(_http, _session);
            await client.CreateShelfAsync("docs");
            IShelfClient shelf = client.Shelf("docs");
            string path = LongestPath('漢');

            await shelf.UploadAsync(path, new MemoryStream(Encoding.UTF8.GetBytes("deep")));
            ShelfFile found = await shelf.GetFileAsync(path);
            FileSearchResults after = await shelf.SearchAsync(new FileSearch { Under = ShelfPath.GetParent(path), After = path });

            Assert.Equal((path, 4L), (found.Path, found.SizeBytes));
            Assert.Empty(after.Files);
        }

        /// <summary>A path of exactly <see cref="ShelfPath.MaxPathLength"/> characters, its names as long as names get.</summary>
        private static string LongestPath(char character)
        {
            StringBuilder path = new StringBuilder();
            while (path.Length < ShelfPath.MaxPathLength)
            {
                int length = Math.Min(ShelfPath.MaxNameLength, ShelfPath.MaxPathLength - path.Length - 1);
                path.Append(ShelfPath.Separator).Append(character, length);
            }

            Assert.Equal(ShelfPath.MaxPathLength, path.Length);
            return path.ToString();
        }
    }
}
