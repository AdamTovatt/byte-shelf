using System.Buffers.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using ByteShelf.Server.Storage.Services;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;
using Microsoft.Extensions.Time.Testing;

namespace ByteShelf.Server.Tests.Storage
{
    /// <summary>Links that let a browser open or save one file without a token, for a few minutes.</summary>
    public sealed class DownloadLinksTests : IAsyncLifetime
    {
        private ServerTestHost _host = null!;

        public async Task InitializeAsync()
        {
            _host = await ServerTestHost.StartAsync();
            (await _host.AdminClient.PostAsJsonAsync("/api/shelves", new CreateShelfRequest { Name = "docs" })).EnsureSuccessStatusCode();
        }

        public async Task DisposeAsync()
        {
            await _host.DisposeAsync();
        }

        [Fact]
        public async Task Link_WithoutAToken_SavesTheFileUnderItsName()
        {
            await PutAsync("/notes/a.txt", "the contents");
            DownloadLink link = await CreateLinkAsync("/notes/a.txt");

            HttpResponseMessage response = await _host.AnonymousClient.GetAsync(link.Url + "?download=true");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("the contents", await response.Content.ReadAsStringAsync());
            Assert.Equal("a.txt", response.Content.Headers.ContentDisposition?.FileName);
            Assert.StartsWith("sandbox", Assert.Single(response.Headers.GetValues("Content-Security-Policy")), StringComparison.Ordinal);
        }

        [Fact]
        public async Task Link_Altered_IsNotFound()
        {
            await PutAsync("/a.txt", "a");
            DownloadLink link = await CreateLinkAsync("/a.txt");

            // A character in the middle: the last one may carry only unused bits.
            HttpResponseMessage response = await _host.AnonymousClient.GetAsync(WithCharacterAt(link.Url, link.Url.Length / 2, 'A', 'B'));

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task Link_CharacterOutsideTheAlphabet_IsNotFound()
        {
            await PutAsync("/a.txt", "a");
            DownloadLink link = await CreateLinkAsync("/a.txt");

            HttpResponseMessage response = await _host.AnonymousClient.GetAsync(WithCharacterAt(link.Url, link.Url.Length / 2, '*', '*'));

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task Link_Range_AnswersThatPart()
        {
            await PutAsync("/a.txt", "the contents");
            DownloadLink link = await CreateLinkAsync("/a.txt");
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, link.Url);
            request.Headers.Range = new RangeHeaderValue(4, 6);

            HttpResponseMessage response = await _host.AnonymousClient.SendAsync(request);

            Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
            Assert.Equal("con", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task Link_MadeById_SavesTheFile()
        {
            ShelfFile file = await PutAsync("/a.txt", "by id");
            HttpResponseMessage created = await _host.AdminClient.PostAsync($"/api/shelves/docs/download-link?id={file.Id}", null);
            DownloadLink link = (await created.Content.ReadFromJsonAsync<DownloadLink>())!;

            HttpResponseMessage response = await _host.AnonymousClient.GetAsync(link.Url);

            Assert.Equal("by id", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task Link_ShelfDeleted_IsNotFound()
        {
            await PutAsync("/a.txt", "a");
            DownloadLink link = await CreateLinkAsync("/a.txt");
            (await _host.AdminClient.DeleteAsync("/api/shelves/docs/files?path=/a.txt")).EnsureSuccessStatusCode();
            (await _host.AdminClient.DeleteAsync("/api/shelves/docs/trash")).EnsureSuccessStatusCode();
            (await _host.AdminClient.DeleteAsync("/api/shelves/docs")).EnsureSuccessStatusCode();

            HttpResponseMessage response = await _host.AnonymousClient.GetAsync(link.Url);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task Link_FileMovedToTheTrash_IsNotFound()
        {
            await PutAsync("/a.txt", "a");
            DownloadLink link = await CreateLinkAsync("/a.txt");
            (await _host.AdminClient.DeleteAsync("/api/shelves/docs/files?path=/a.txt")).EnsureSuccessStatusCode();

            HttpResponseMessage response = await _host.AnonymousClient.GetAsync(link.Url);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task CreateLink_NoAccessToTheShelf_IsNotFound()
        {
            await PutAsync("/a.txt", "a");

            HttpResponseMessage response = await _host.MemberClient.PostAsync("/api/shelves/docs/download-link?path=/a.txt", null);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task TryRead_UntilItRunsOut_AnswersTheShelfAndFile()
        {
            await using TestRoot root = TestRoot.Create();
            FakeTimeProvider clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero));
            DownloadLinks links = new DownloadLinks(root.Root, clock);
            Guid shelfId = Guid.NewGuid();
            Guid fileId = Guid.NewGuid();
            string token = TokenOf(links.Create(shelfId, fileId));

            clock.Advance(DownloadLinks.Lifetime - TimeSpan.FromSeconds(1));
            bool readBefore = links.TryRead(token, out Guid readShelfId, out Guid readFileId);
            clock.Advance(TimeSpan.FromSeconds(1));
            bool readAfter = links.TryRead(token, out _, out _);

            Assert.Equal((true, shelfId, fileId, false), (readBefore, readShelfId, readFileId, readAfter));
        }

        [Fact]
        public async Task TryRead_LinkFromAnotherRoot_IsRefused()
        {
            await using TestRoot first = TestRoot.Create();
            await using TestRoot second = TestRoot.Create();
            string token = TokenOf(new DownloadLinks(first.Root, TimeProvider.System).Create(Guid.NewGuid(), Guid.NewGuid()));

            Assert.False(new DownloadLinks(second.Root, TimeProvider.System).TryRead(token, out _, out _));
        }

        [Fact]
        public async Task TryRead_AfterARestartOnTheSameRoot_ReadsEarlierLinks()
        {
            await using TestRoot root = TestRoot.Create();
            Guid fileId = Guid.NewGuid();
            string token = TokenOf(new DownloadLinks(root.Root, TimeProvider.System).Create(Guid.NewGuid(), fileId));

            bool read = new DownloadLinks(root.Root, TimeProvider.System).TryRead(token, out _, out Guid readFileId);

            Assert.Equal((true, fileId), (read, readFileId));
        }

        [Theory]
        [InlineData("")]
        [InlineData("not-a-token")]
        [InlineData("AAAA")]
        public async Task TryRead_NotALink_IsRefused(string token)
        {
            await using TestRoot root = TestRoot.Create();

            Assert.False(new DownloadLinks(root.Root, TimeProvider.System).TryRead(token, out _, out _));
        }

        [Fact]
        public async Task TryRead_SignedPartChanged_IsRefused()
        {
            await using TestRoot root = TestRoot.Create();
            DownloadLinks links = new DownloadLinks(root.Root, TimeProvider.System);
            byte[] bytes = Base64Url.DecodeFromChars(TokenOf(links.Create(Guid.NewGuid(), Guid.NewGuid())));
            bytes[20] ^= 1;

            Assert.False(links.TryRead(Base64Url.EncodeToString(bytes), out _, out _));
        }

        private static string TokenOf(DownloadLink link)
        {
            return link.Url[$"/{DownloadLinks.Route}/".Length..];
        }

        /// <summary><paramref name="url"/> with the character at <paramref name="index"/> replaced by one that differs from it.</summary>
        private static string WithCharacterAt(string url, int index, char replacement, char otherwise)
        {
            char character = url[index] == replacement ? otherwise : replacement;
            return url[..index] + character + url[(index + 1)..];
        }

        private async Task<ShelfFile> PutAsync(string path, string text)
        {
            using StringContent content = new StringContent(text, Encoding.UTF8, "text/plain");
            HttpResponseMessage response = await _host.AdminClient.PutAsync($"/api/shelves/docs/content?path={Uri.EscapeDataString(path)}", content);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<ShelfFile>())!;
        }

        private async Task<DownloadLink> CreateLinkAsync(string path)
        {
            HttpResponseMessage response = await _host.AdminClient.PostAsync($"/api/shelves/docs/download-link?path={Uri.EscapeDataString(path)}", null);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<DownloadLink>())!;
        }
    }
}
