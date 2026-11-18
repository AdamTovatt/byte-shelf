using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using ByteShelf.Server.Shared;
using ByteShelf.Server.Storage.Services;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;
using Microsoft.Extensions.DependencyInjection;

namespace ByteShelf.Server.Tests.Storage
{
    /// <summary>Links that let anyone holding one open or save one file without a token, until they run out or are revoked.</summary>
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
        public async Task Link_FileEncryptedInPlaceSince_Returns409()
        {
            ShelfFile file = await PutAsync("/a.txt", "plain contents");
            DownloadLink link = await CreateLinkAsync("/a.txt");
            using StringContent encrypted = new StringContent("age-encryption.org/v1\nencrypted contents");
            (await _host.AdminClient.PutAsync($"/api/shelves/docs/content/swap?id={file.Id}&sha256={file.Sha256}&encrypted=true", encrypted)).EnsureSuccessStatusCode();

            HttpResponseMessage response = await _host.AnonymousClient.GetAsync(link.Url);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }

        [Fact]
        public async Task Link_WithoutAPublicUrl_IsAPathFromTheServersRoot()
        {
            await PutAsync("/a.txt", "a");

            DownloadLink link = await CreateLinkAsync("/a.txt");

            Assert.StartsWith("/api/downloads/", link.Url, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Link_WithAPublicUrl_IsOnItBelowItsPathAndServesTheFile()
        {
            await using ServerTestHost host = await ServerTestHost.StartAsync(services => services.AddSingleton(new PublicAddress("https://files.example.com/shelf")));
            (await host.AdminClient.PostAsJsonAsync("/api/shelves", new CreateShelfRequest { Name = "docs" })).EnsureSuccessStatusCode();
            (await host.AdminClient.PutAsync("/api/shelves/docs/content?path=/a.txt", new StringContent("public"))).EnsureSuccessStatusCode();

            HttpResponseMessage response = await host.AdminClient.PostAsync("/api/shelves/docs/download-link?path=/a.txt", null);
            response.EnsureSuccessStatusCode();
            Uri link = new Uri((await response.Content.ReadFromJsonAsync<DownloadLink>())!.Url);

            Assert.Equal("https://files.example.com", link.GetLeftPart(UriPartial.Authority));
            Assert.StartsWith("/shelf/api/downloads/", link.AbsolutePath, StringComparison.Ordinal);
            Assert.Equal("public", await host.AnonymousClient.GetStringAsync(link.AbsolutePath["/shelf".Length..]));
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
        public async Task CreateLink_NoLifetime_RunsOutAfterADay()
        {
            await PutAsync("/a.txt", "a");
            DateTimeOffset before = DateTimeOffset.UtcNow;

            DownloadLink link = await CreateLinkAsync("/a.txt");

            Assert.InRange(link.ExpiresAt, before.AddHours(24), DateTimeOffset.UtcNow.AddHours(24));
        }

        [Fact]
        public async Task Link_Revoked_IsNotFound()
        {
            await PutAsync("/a.txt", "a");
            DownloadLink link = await CreateLinkAsync("/a.txt");

            HttpResponseMessage revoked = await _host.AdminClient.DeleteAsync($"/api/shelves/docs/links/{link.Id}");
            HttpResponseMessage response = await _host.AnonymousClient.GetAsync(link.Url);

            Assert.Equal((HttpStatusCode.NoContent, HttpStatusCode.NotFound), (revoked.StatusCode, response.StatusCode));
        }

        [Fact]
        public async Task Links_ListedForAReadKey_AreItsOwnWithKindMakerAndExpiry()
        {
            await PutAsync("/a.txt", "a");
            using HttpClient key = await _host.CreateKeyClientAsync("docs", ShelfKeyPermission.Read);
            HttpResponseMessage made = await key.PostAsync("/api/shelves/docs/download-link?path=/a.txt&expiresInMinutes=5", null);
            DownloadLink own = (await made.Content.ReadFromJsonAsync<DownloadLink>())!;
            await CreateLinkAsync("/a.txt");

            ShelfLink listed = Assert.Single((await key.GetFromJsonAsync<IReadOnlyList<ShelfLink>>("/api/shelves/docs/links"))!);

            Assert.Equal((own.Id, ShelfLinkKind.Download, "/a.txt", LinkMakerKind.Key, true, own.ExpiresAt), (listed.Id, listed.Kind, listed.Path, listed.CreatedBy.Kind, listed.CreatedBy.IsCaller, listed.ExpiresAt));
        }

        [Fact]
        public async Task Links_Listed_NameTheirKindsInLowercase()
        {
            await PutAsync("/a.txt", "a");
            await CreateLinkAsync("/a.txt");

            string json = await _host.AdminClient.GetStringAsync("/api/shelves/docs/links");

            Assert.Contains("\"kind\":\"download\"", json, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Links_Listed_NeverCarryTheirToken()
        {
            await PutAsync("/a.txt", "a");
            DownloadLink link = await CreateLinkAsync("/a.txt");

            string json = await _host.AdminClient.GetStringAsync("/api/shelves/docs/links");

            Assert.Contains(link.Id.ToString("D"), json, StringComparison.Ordinal);
            Assert.DoesNotContain(link.Url[$"/{DownloadLinks.Route}/".Length..], json, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Link_ItsUploadLinkToken_IsNotFound()
        {
            HttpResponseMessage made = await _host.AdminClient.PostAsJsonAsync("/api/shelves/docs/upload-link", new CreateUploadLinkRequest { Path = "/a.txt" });
            UploadLink uploadLink = (await made.Content.ReadFromJsonAsync<UploadLink>())!;
            string token = uploadLink.Url[$"/{UploadLinks.Route}/".Length..];

            HttpResponseMessage response = await _host.AnonymousClient.GetAsync($"/{DownloadLinks.Route}/{token}");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task CreateLink_ExpiresInMinutes_RunsOutThen()
        {
            await PutAsync("/a.txt", "a");
            DateTimeOffset before = DateTimeOffset.UtcNow;

            HttpResponseMessage response = await _host.AdminClient.PostAsync("/api/shelves/docs/download-link?path=/a.txt&expiresInMinutes=10080", null);
            response.EnsureSuccessStatusCode();
            DownloadLink link = (await response.Content.ReadFromJsonAsync<DownloadLink>())!;

            Assert.InRange(link.ExpiresAt, before.AddDays(7), DateTimeOffset.UtcNow.AddDays(7));
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
        public async Task CreateLink_NoAccessToTheShelf_IsNotFound()
        {
            await PutAsync("/a.txt", "a");

            HttpResponseMessage response = await _host.MemberClient.PostAsync("/api/shelves/docs/download-link?path=/a.txt", null);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
