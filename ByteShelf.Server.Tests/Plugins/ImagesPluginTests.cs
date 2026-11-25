using System.Net;
using System.Text.Json;
using ByteShelf.Server.Tests.TestUtilities;

namespace ByteShelf.Server.Tests.Plugins
{
    /// <summary>The images plugin as a server runs it: loaded from its folder into its own process, with the native library and the policy in it. What it makes is tested in its own tests.</summary>
    public sealed class ImagesPluginTests
    {
        private const string PngOf40By20 = "iVBORw0KGgoAAAANSUhEUgAAACgAAAAUCAIAAABwJOjsAAAAI0lEQVR42u3NsQ0AAAjAoP7/tJ7hIAk7TZ1ILBaLxWKx+E+8ffodDqIehz0AAAAASUVORK5CYII=";

        private static readonly string BuiltPlugins = Path.Combine(AppContext.BaseDirectory, "images-plugin");

        [Theory]
        [InlineData("thumbnail", "image/webp")]
        [InlineData("preview", "image/webp")]
        [InlineData("agent", "image/jpeg")]
        public async Task AnImage_HasWhatItMakes(string kind, string mediaType)
        {
            await using ServerTestHost host = await StartWithAnImageAsync();

            HttpResponseMessage made = await host.AdminClient.GetAsync($"/api/shelves/docs/files/derived?path=/a.png&kind={kind}");

            Assert.Equal((HttpStatusCode.OK, mediaType), (made.StatusCode, made.Content.Headers.ContentType?.MediaType));
            Assert.NotEmpty(await made.Content.ReadAsByteArrayAsync());
        }

        [Fact]
        public async Task AnImage_HasWhatItReads()
        {
            await using ServerTestHost host = await StartWithAnImageAsync();

            await host.Services.ReadPluginMetadataAsync();

            using JsonDocument metadata = JsonDocument.Parse(await host.AdminClient.GetStringAsync("/api/shelves/docs/files/metadata?path=/a.png"));
            JsonElement document = metadata.RootElement.GetProperty("plugins").GetProperty("images").GetProperty("contentsDocument");
            Assert.Equal((40, 20), (document.GetProperty("width").GetInt32(), document.GetProperty("height").GetInt32()));
        }

        private static async Task<ServerTestHost> StartWithAnImageAsync()
        {
            ServerTestHost host = await ServerTestHost.StartAsync(pluginsPath: BuiltPlugins);
            await host.CreateShelfAsync("docs");
            await host.PutAsAdminAsync("docs", "/a.png", Convert.FromBase64String(PngOf40By20), "image/png");
            return host;
        }
    }
}
