using System.Net;
using System.Net.Http.Json;
using System.Text;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.Storage
{
    /// <summary>Writing a plugin's document about a file over HTTP: who may, and what is refused before it is written.</summary>
    public sealed class PluginMetadataEndpointTests : IAsyncLifetime
    {
        private const string Url = "/api/shelves/docs/files/plugin-metadata?path=/a.txt&plugin=" + TestPluginMetadata.Plugin;

        private ServerTestHost _host = null!;
        private ShelfFile _file = null!;

        public async Task InitializeAsync()
        {
            _host = await ServerTestHost.StartAsync(services => new TestMetadataPlugins(TestMetadataPlugins.Declaration).AddTo(services));
            await _host.CreateShelfAsync("docs");
            _file = await _host.PutAsAdminAsync("docs", "/a.txt", "a"u8.ToArray(), "text/plain");
        }

        public async Task DisposeAsync()
        {
            await _host.DisposeAsync();
        }

        [Theory]
        [InlineData(ShelfPermission.Read, HttpStatusCode.Forbidden)]
        [InlineData(ShelfPermission.AddOnly, HttpStatusCode.Forbidden)]
        [InlineData(ShelfPermission.ReadWrite, HttpStatusCode.OK)]
        public async Task Put_NeedsWriteAccess(ShelfPermission permission, HttpStatusCode answered)
        {
            using HttpClient key = await _host.CreateKeyClientAsync("docs", permission);

            HttpResponseMessage response = await PutAsync(key, "{\"document\":{\"rating\":4},\"expectedVersion\":null}");

            Assert.Equal(answered, response.StatusCode);
            Assert.Equal(answered == HttpStatusCode.OK ? "{\"rating\":4}" : null, await _host.Services.GetFileDocumentAsync(_file.Id));
        }

        [Fact]
        public async Task Put_ByAUserWithoutTheShelf_IsNotFound()
        {
            HttpResponseMessage response = await PutAsync(_host.MemberClient, "{\"document\":{}}");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task Get_ByAKeyThatMayRead_HasTheDocuments()
        {
            await _host.Services.SetFileDocumentAsync(_file.Id, "{\"rating\":4}");
            await _host.Services.RecordContentDocumentAsync(_file.Sha256, "{\"width\":10}");
            using HttpClient key = await _host.CreateKeyClientAsync("docs", ShelfPermission.Read);

            string json = await key.GetStringAsync("/api/shelves/docs/files/metadata?path=/a.txt");

            Assert.Contains("\"contentsDocument\":{\"width\":10},\"fileDocument\":{\"rating\":4},\"fileVersion\":1,", json, StringComparison.Ordinal);
            Assert.Contains("{\"name\":\"rating\",\"label\":\"Rating\",\"kind\":\"number\",\"document\":\"file\",\"value\":{\"number\":4}}", json, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("[1]")]
        [InlineData("\"text\"")]
        [InlineData("{\"t\":\"\\ud83c\"}")]
        [InlineData(null)]
        public async Task Put_SomethingThatIsNoDocument_IsABadRequest(string? document)
        {
            string sent = document ?? $"{{\"t\":\"{new string('x', PluginDocument.MaxBytes)}\"}}";

            HttpResponseMessage response = await PutAsync(_host.AdminClient, $"{{\"document\":{sent}}}");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Null(await _host.Services.GetFileDocumentAsync(_file.Id));
        }

        [Fact]
        public async Task Put_BytesThatAreNoUtf8_IsABadRequest()
        {
            using ByteArrayContent body = new ByteArrayContent([.. "{\"document\":{\"t\":\""u8, 0xC3, 0x28, .. "\"}}"u8]);
            body.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

            HttpResponseMessage response = await _host.AdminClient.PutAsync(Url, body);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Null(await _host.Services.GetFileDocumentAsync(_file.Id));
        }

        [Theory]
        [InlineData("{}")]
        [InlineData("{\"expectedVersion\":1}")]
        public async Task Put_WithoutADocument_IsABadRequestAndRemovesNothing(string body)
        {
            await _host.Services.SetFileDocumentAsync(_file.Id, "{\"rating\":4}");

            HttpResponseMessage response = await PutAsync(_host.AdminClient, body);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("{\"rating\":4}", await _host.Services.GetFileDocumentAsync(_file.Id));
        }

        [Fact]
        public async Task Put_NullDocument_RemovesIt()
        {
            await _host.Services.SetFileDocumentAsync(_file.Id, "{\"rating\":4}");

            HttpResponseMessage response = await PutAsync(_host.AdminClient, "{\"document\":null,\"expectedVersion\":1}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Null(await _host.Services.GetFileDocumentAsync(_file.Id));
        }

        [Fact]
        public async Task Put_WithoutAPlugin_IsABadRequest()
        {
            using StringContent body = new StringContent("{\"document\":{}}", Encoding.UTF8, "application/json");

            HttpResponseMessage response = await _host.AdminClient.PutAsync("/api/shelves/docs/files/plugin-metadata?path=/a.txt", body);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Theory]
        [InlineData("search")]
        [InlineData("duplicates")]
        public async Task Search_BlankFilterInTheQuery_IsABadRequest(string endpoint)
        {
            HttpResponseMessage response = await _host.AdminClient.GetAsync($"/api/shelves/docs/{endpoint}?pluginFields=");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Theory]
        [InlineData("test-plugin.rating=one", HttpStatusCode.BadRequest)]
        [InlineData("test-plugin.rating=..", HttpStatusCode.BadRequest)]
        [InlineData("test-plugin.rating=1", HttpStatusCode.OK)]
        public async Task Search_FilterInTheQuery_IsReadOrABadRequest(string filter, HttpStatusCode answered)
        {
            HttpResponseMessage response = await _host.AdminClient.GetAsync($"/api/shelves/docs/search?pluginFields={Uri.EscapeDataString(filter)}");

            Assert.Equal(answered, response.StatusCode);
        }

        [Fact]
        public async Task Search_TwoFilters_AreBothRead()
        {
            await _host.Services.SetFileDocumentAsync(_file.Id, "{\"rating\":4}");

            FileSearchResults? one = await _host.AdminClient.GetFromJsonAsync<FileSearchResults>("/api/shelves/docs/search?pluginFields=test-plugin.rating");
            FileSearchResults? both = await _host.AdminClient.GetFromJsonAsync<FileSearchResults>("/api/shelves/docs/search?pluginFields=test-plugin.rating&pluginFields=test-plugin.done");

            Assert.Equal((1, 0), (one!.Files.Count, both!.Files.Count));
        }

        private static async Task<HttpResponseMessage> PutAsync(HttpClient client, string json)
        {
            using StringContent body = new StringContent(json, Encoding.UTF8, "application/json");
            return await client.PutAsync(Url, body);
        }
    }
}
