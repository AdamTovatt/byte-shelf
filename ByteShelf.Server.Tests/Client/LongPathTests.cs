using System.Text;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfClient;
using ByteShelfClient.Http;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.Client
{
    /// <summary>
    /// Paths as long as a shelf allows, over real HTTP: the in-memory test server has no request-line
    /// limit, so only Kestrel shows whether one fits. The names are non-ASCII, the longest a path gets
    /// once percent-encoded in a query string.
    /// </summary>
    public sealed class LongPathTests : IAsyncLifetime
    {
        private KestrelTestHost _host = null!;
        private UserSession _session = null!;

        public async Task InitializeAsync()
        {
            _host = await KestrelTestHost.StartAsync();
            _session = await UserSession.SignInAsync(_host.Http, ServerTestHost.AdminEmail, ServerTestHost.Password);
        }

        public async Task DisposeAsync()
        {
            _session.Dispose();
            await _host.DisposeAsync();
        }

        [Fact]
        public async Task LongestPath_OfThreeByteCharacters_IsStoredFoundAndSearchedPast()
        {
            IByteShelfClient client = new ByteShelfHttpClient(_host.Http, _session);
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
