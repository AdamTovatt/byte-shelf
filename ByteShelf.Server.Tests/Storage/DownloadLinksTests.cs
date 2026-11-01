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
    /// <summary>Links that let anyone holding one open or save one file without a token, until they run out.</summary>
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
        public async Task Link_FileRestoredFromTheTrash_WorksAgain()
        {
            await PutAsync("/a.txt", "a");
            DownloadLink link = await CreateLinkAsync("/a.txt");
            HttpResponseMessage deleted = await _host.AdminClient.DeleteAsync("/api/shelves/docs/files?path=/a.txt");
            TrashEntry entry = (await deleted.Content.ReadFromJsonAsync<TrashEntry>())!;
            (await _host.AdminClient.PostAsync($"/api/shelves/docs/trash/{entry.DeletionId}/restore", null)).EnsureSuccessStatusCode();

            HttpResponseMessage response = await _host.AnonymousClient.GetAsync(link.Url);

            Assert.Equal("a", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task Link_FileStoredOver_IsNotFound()
        {
            await PutAsync("/a.txt", "old");
            DownloadLink link = await CreateLinkAsync("/a.txt");
            using StringContent content = new StringContent("new", Encoding.UTF8, "text/plain");
            (await _host.AdminClient.PutAsync("/api/shelves/docs/content?path=/a.txt&overwrite=true", content)).EnsureSuccessStatusCode();

            HttpResponseMessage response = await _host.AnonymousClient.GetAsync(link.Url);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task Create_PartWayThroughASecond_AnswersTheSecondTheLinkStopsWorkingAt()
        {
            await using TestRoot root = TestRoot.Create();
            FakeTimeProvider clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 31, 12, 0, 0, 700, TimeSpan.Zero));
            DownloadLinks links = new DownloadLinks(root.Root, clock);

            DownloadLink link = links.Create(Guid.NewGuid(), Guid.NewGuid(), TimeSpan.FromMinutes(10));
            clock.SetUtcNow(link.ExpiresAt - TimeSpan.FromMilliseconds(1));
            bool readBefore = links.TryRead(TokenOf(link), out _, out _);
            clock.SetUtcNow(link.ExpiresAt);
            bool readAt = links.TryRead(TokenOf(link), out _, out _);

            Assert.Equal((new DateTimeOffset(2026, 10, 31, 12, 10, 0, TimeSpan.Zero), true, false), (link.ExpiresAt, readBefore, readAt));
        }

        [Fact]
        public async Task CreateLink_NoLifetime_RunsOutAfterADay()
        {
            await PutAsync("/a.txt", "a");
            DateTimeOffset before = DateTimeOffset.UtcNow;

            DownloadLink link = await CreateLinkAsync("/a.txt");

            // The expiry is cut to the whole second the token keeps.
            Assert.InRange(link.ExpiresAt, before.AddHours(24).AddSeconds(-1), DateTimeOffset.UtcNow.AddHours(24));
        }

        [Fact]
        public async Task CreateLink_ExpiresInMinutes_RunsOutThen()
        {
            await PutAsync("/a.txt", "a");
            DateTimeOffset before = DateTimeOffset.UtcNow;

            HttpResponseMessage response = await _host.AdminClient.PostAsync("/api/shelves/docs/download-link?path=/a.txt&expiresInMinutes=10080", null);
            response.EnsureSuccessStatusCode();
            DownloadLink link = (await response.Content.ReadFromJsonAsync<DownloadLink>())!;

            // The expiry is cut to the whole second the token keeps.
            Assert.InRange(link.ExpiresAt, before.AddDays(7).AddSeconds(-1), DateTimeOffset.UtcNow.AddDays(7));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-5)]
        [InlineData(10081)]
        public async Task CreateLink_LifetimeOutOfBounds_Returns400(int expiresInMinutes)
        {
            await PutAsync("/a.txt", "a");

            HttpResponseMessage response = await _host.AdminClient.PostAsync($"/api/shelves/docs/download-link?path=/a.txt&expiresInMinutes={expiresInMinutes}", null);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(ErrorCodes.InvalidRequest, (await response.Content.ReadFromJsonAsync<ErrorResponse>())?.Code);
        }

        [Fact]
        public async Task Create_LifetimeOutOfBounds_ThrowsArgumentException()
        {
            await using TestRoot root = TestRoot.Create();
            DownloadLinks links = new DownloadLinks(root.Root, TimeProvider.System);

            Assert.Throws<ArgumentException>(() => links.Create(Guid.NewGuid(), Guid.NewGuid(), TimeSpan.FromDays(8)));
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
            string token = TokenOf(links.Create(shelfId, fileId, TimeSpan.FromDays(7)));

            clock.Advance(TimeSpan.FromDays(7) - TimeSpan.FromSeconds(1));
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
            string token = TokenOf(new DownloadLinks(first.Root, TimeProvider.System).Create(Guid.NewGuid(), Guid.NewGuid(), DownloadLink.DefaultLifetime));

            Assert.False(new DownloadLinks(second.Root, TimeProvider.System).TryRead(token, out _, out _));
        }

        [Fact]
        public async Task TryRead_AfterARestartOnTheSameRoot_ReadsEarlierLinks()
        {
            await using TestRoot root = TestRoot.Create();
            Guid fileId = Guid.NewGuid();
            string token = TokenOf(new DownloadLinks(root.Root, TimeProvider.System).Create(Guid.NewGuid(), fileId, DownloadLink.DefaultLifetime));

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
            byte[] bytes = Base64Url.DecodeFromChars(TokenOf(links.Create(Guid.NewGuid(), Guid.NewGuid(), DownloadLink.DefaultLifetime)));
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
