using System.Buffers.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using ByteShelf.Server.Access.Models;
using ByteShelf.Server.Shared;
using ByteShelf.Server.Shared.Auth;
using ByteShelf.Server.Shared.Root;
using ByteShelf.Server.Storage.Services;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;
using Microsoft.Extensions.Time.Testing;

namespace ByteShelf.Server.Tests.Storage
{
    /// <summary>Links that let anyone holding one store one file without a token, acting as whoever made the link.</summary>
    public sealed class UploadLinksTests : IAsyncLifetime
    {
        private static readonly PublicAddress Unconfigured = new PublicAddress(null);

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
        public async Task Link_WithoutAToken_StoresTheFileWithItsDescriptionAndTheTypeItsPathNames()
        {
            UploadLink link = await CreateLinkAsync(_host.AdminClient, new CreateUploadLinkRequest { Path = "/in/report.pdf", Description = "Q3 report, från Agnes" });

            HttpResponseMessage response = await _host.AnonymousClient.PutAsync(link.Url, new ByteArrayContent(Encoding.UTF8.GetBytes("%PDF-1.7")));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            ShelfFile stored = (await response.Content.ReadFromJsonAsync<ShelfFile>())!;
            Assert.Equal(("/in/report.pdf", "application/pdf", "Q3 report, från Agnes", 8L), (stored.Path, stored.ContentType, stored.Description, stored.SizeBytes));
        }

        [Fact]
        public async Task Link_ContentTypeSent_StoresThatType()
        {
            UploadLink link = await CreateLinkAsync(_host.AdminClient, new CreateUploadLinkRequest { Path = "/notes.txt" });
            ByteArrayContent content = new ByteArrayContent(Encoding.UTF8.GetBytes("# notes"));
            content.Headers.ContentType = new MediaTypeHeaderValue("text/markdown");

            ShelfFile stored = (await (await _host.AnonymousClient.PutAsync(link.Url, content)).Content.ReadFromJsonAsync<ShelfFile>())!;

            Assert.Equal("text/markdown", stored.ContentType);
        }

        [Fact]
        public async Task CreateLink_NoLifetime_IsAPathFromTheServersRootThatRunsOutAfterTheDefault()
        {
            DateTimeOffset before = DateTimeOffset.UtcNow;

            UploadLink link = await CreateLinkAsync(_host.AdminClient, new CreateUploadLinkRequest { Path = "/a.txt" });

            Assert.StartsWith("/api/upload-links/", link.Url, StringComparison.Ordinal);
            // The expiry is cut to the whole second the token keeps.
            Assert.InRange(link.ExpiresAt, before + UploadLink.Lifetime.Default - TimeSpan.FromSeconds(1), DateTimeOffset.UtcNow + UploadLink.Lifetime.Default);
            Assert.Equal(("/a.txt", false), (link.Path, link.Overwrite));
        }

        [Theory]
        [InlineData(0)]
        [InlineData((24 * 60) + 1)]
        public async Task CreateLink_LifetimeOutOfBounds_Returns400(int expiresInMinutes)
        {
            Assert.True(expiresInMinutes < UploadLink.Lifetime.Min.TotalMinutes || expiresInMinutes > UploadLink.Lifetime.Max.TotalMinutes);

            HttpResponseMessage response = await _host.AdminClient.PostAsJsonAsync(
                "/api/shelves/docs/upload-link", new CreateUploadLinkRequest { Path = "/a.txt", ExpiresInMinutes = expiresInMinutes });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task CreateLink_ForTheRoot_Returns400()
        {
            HttpResponseMessage response = await _host.AdminClient.PostAsJsonAsync("/api/shelves/docs/upload-link", new CreateUploadLinkRequest { Path = "/" });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task CreateLink_DescriptionTooLongToCarry_Returns400NamingTheLimit()
        {
            // 1,500 characters is within a description's limit, but three bytes each is past what a link carries.
            string description = new string('€', 1500);

            HttpResponseMessage response = await _host.AdminClient.PostAsJsonAsync(
                "/api/shelves/docs/upload-link", new CreateUploadLinkRequest { Path = "/a.txt", Description = description });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains($"{NormalizedUploadLinkRequest.MaxCarriedBytes} bytes", (await response.Content.ReadFromJsonAsync<ErrorResponse>())!.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task CreateLink_AddOnlyKeyAsksToOverwrite_Returns403()
        {
            using HttpClient key = await _host.CreateKeyClientAsync("docs", ShelfKeyPermission.AddOnly);

            HttpResponseMessage response = await key.PostAsJsonAsync("/api/shelves/docs/upload-link", new CreateUploadLinkRequest { Path = "/a.txt", Overwrite = true });

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task CreateLink_ReadKey_Returns403()
        {
            using HttpClient key = await _host.CreateKeyClientAsync("docs", ShelfKeyPermission.Read);

            HttpResponseMessage response = await key.PostAsJsonAsync("/api/shelves/docs/upload-link", new CreateUploadLinkRequest { Path = "/a.txt" });

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task Link_FromAnAddOnlyKey_PathTaken_Returns409AndKeepsTheFile()
        {
            using HttpClient key = await _host.CreateKeyClientAsync("docs", ShelfKeyPermission.AddOnly);
            UploadLink link = await CreateLinkAsync(key, new CreateUploadLinkRequest { Path = "/a.txt" });
            (await PutAsync(link, "first")).EnsureSuccessStatusCode();

            HttpResponseMessage second = await PutAsync(link, "second");

            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
            Assert.Equal("first", await _host.AdminClient.GetStringAsync("/api/shelves/docs/content?path=/a.txt"));
        }

        [Fact]
        public async Task Link_ThatOverwrites_ReplacesTheFile()
        {
            UploadLink link = await CreateLinkAsync(_host.AdminClient, new CreateUploadLinkRequest { Path = "/a.txt", Overwrite = true });
            (await PutAsync(link, "first")).EnsureSuccessStatusCode();

            (await PutAsync(link, "second")).EnsureSuccessStatusCode();

            Assert.Equal("second", await _host.AdminClient.GetStringAsync("/api/shelves/docs/content?path=/a.txt"));
        }

        [Fact]
        public async Task Link_KeyDeletedSince_Returns404()
        {
            CreatedShelfKey created = await _host.CreateKeyAsync("docs", ShelfKeyPermission.ReadWrite);
            using HttpClient key = _host.CreateKeyClient(created.Secret);
            UploadLink link = await CreateLinkAsync(key, new CreateUploadLinkRequest { Path = "/a.txt" });
            (await _host.AdminClient.DeleteAsync($"/api/shelves/docs/keys/{created.Key.Id}")).EnsureSuccessStatusCode();

            HttpResponseMessage response = await PutAsync(link, "a");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task Link_MakerMayOnlyReadNow_Returns403()
        {
            await _host.GrantMemberAsync("docs", ShelfAccessPermission.ReadWrite);
            UploadLink link = await CreateLinkAsync(_host.MemberClient, new CreateUploadLinkRequest { Path = "/a.txt" });
            await _host.GrantMemberAsync("docs", ShelfAccessPermission.Read);

            HttpResponseMessage response = await PutAsync(link, "a");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task Link_MakersAccessTakenAway_Returns404()
        {
            await _host.GrantMemberAsync("docs", ShelfAccessPermission.ReadWrite);
            UploadLink link = await CreateLinkAsync(_host.MemberClient, new CreateUploadLinkRequest { Path = "/a.txt" });
            (await _host.AdminClient.DeleteAsync($"/api/shelves/docs/access/{_host.MemberId}")).EnsureSuccessStatusCode();

            HttpResponseMessage response = await PutAsync(link, "a");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _host.AdminClient.GetAsync("/api/shelves/docs/files?path=/a.txt")).StatusCode);
        }

        [Fact]
        public async Task Link_OverQuota_Returns507()
        {
            (await _host.AdminClient.PostAsJsonAsync("/api/shelves", new CreateShelfRequest { Name = "small", QuotaBytes = 4 })).EnsureSuccessStatusCode();
            HttpResponseMessage created = await _host.AdminClient.PostAsJsonAsync("/api/shelves/small/upload-link", new CreateUploadLinkRequest { Path = "/a.txt" });
            UploadLink link = (await created.Content.ReadFromJsonAsync<UploadLink>())!;

            HttpResponseMessage response = await PutAsync(link, "five!");

            Assert.Equal(HttpStatusCode.InsufficientStorage, response.StatusCode);
        }

        [Fact]
        public async Task Link_WithoutContentLength_Returns411()
        {
            UploadLink link = await CreateLinkAsync(_host.AdminClient, new CreateUploadLinkRequest { Path = "/a.txt" });
            StreamContent content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes("a")));
            content.Headers.ContentLength = null;
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Put, link.Url) { Content = content };
            request.Headers.TransferEncodingChunked = true;

            HttpResponseMessage response = await _host.AnonymousClient.SendAsync(request);

            Assert.Equal(HttpStatusCode.LengthRequired, response.StatusCode);
        }

        [Fact]
        public async Task Link_PathInItChanged_Returns404AndStoresNothing()
        {
            UploadLink link = await CreateLinkAsync(_host.AdminClient, new CreateUploadLinkRequest { Path = "/a.txt" });
            byte[] token = Base64Url.DecodeFromChars(TokenOf(link));
            // The expiry's 8 bytes, then the payload, whose path starts at byte 36: "/a.txt" becomes "/b.txt".
            Assert.Equal((byte)'a', token[8 + 36 + 1]);
            token[8 + 36 + 1] = (byte)'b';

            HttpResponseMessage response = await _host.AnonymousClient.PutAsync(
                $"/{UploadLinks.Route}/{Base64Url.EncodeToString(token)}", new ByteArrayContent(Encoding.UTF8.GetBytes("a")));

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _host.AdminClient.GetAsync("/api/shelves/docs/files?path=/b.txt")).StatusCode);
        }

        [Fact]
        public async Task TryRead_UntilItRunsOut_AnswersWhatTheLinkGrants()
        {
            await using TestRoot root = TestRoot.Create();
            FakeTimeProvider clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero));
            UploadLinks links = new UploadLinks(new LinkTokens(root.Root, clock), Unconfigured);
            Guid shelfId = Guid.NewGuid();
            ShelfCallerReference caller = new ShelfCallerReference(ShelfCallerKind.Key, Guid.NewGuid());
            UploadLink link = links.Create(shelfId, caller, new CreateUploadLinkRequest { Path = "/räksmörgås/a.txt", Overwrite = true, Description = "  en beskrivning  ", ExpiresInMinutes = 60 }.Normalize());

            clock.Advance(TimeSpan.FromMinutes(60) - TimeSpan.FromSeconds(1));
            bool readBefore = links.TryRead(TokenOf(link), out UploadLinkGrant? grant);
            clock.Advance(TimeSpan.FromSeconds(1));
            bool readAfter = links.TryRead(TokenOf(link), out _);

            Assert.Equal((true, false), (readBefore, readAfter));
            Assert.Equal(new UploadLinkGrant(shelfId, caller, "/räksmörgås/a.txt", true, "en beskrivning"), grant);
        }

        [Fact]
        public async Task LinkTokens_TokenOfAnotherKind_IsRefused()
        {
            await using TestRoot root = TestRoot.Create();
            LinkTokens tokens = new LinkTokens(root.Root, TimeProvider.System);
            (string token, _) = tokens.Create("download", new byte[64], TimeSpan.FromMinutes(5));

            bool readAsItsOwnKind = tokens.TryRead("download", token, out _);
            bool readAsAnother = tokens.TryRead("upload", token, out _);

            Assert.Equal((true, false), (readAsItsOwnKind, readAsAnother));
        }

        [Fact]
        public async Task LinkTokens_OverTheLongestTokenAnyLinkHas_IsRefusedThoughSigned()
        {
            await using TestRoot root = TestRoot.Create();
            LinkTokens tokens = new LinkTokens(root.Root, TimeProvider.System);
            // A token is the 8-byte expiry, the payload and a 32-byte signature, at most 8 KiB in all.
            (string longest, _) = tokens.Create("upload", new byte[8192 - 40], TimeSpan.FromMinutes(5));
            (string tooLong, _) = tokens.Create("upload", new byte[8192 - 39], TimeSpan.FromMinutes(5));

            Assert.Equal((true, false), (tokens.TryRead("upload", longest, out _), tokens.TryRead("upload", tooLong, out _)));
        }

        [Theory]
        [InlineData(35, 0)]
        [InlineData(36, 1)]
        public async Task TryRead_SignedPayloadShorterThanItsPath_IsRefused(int payloadBytes, int pathLength)
        {
            await using TestRoot root = TestRoot.Create();
            LinkTokens tokens = new LinkTokens(root.Root, TimeProvider.System);
            byte[] payload = new byte[payloadBytes];
            if (payloadBytes >= 36)
            {
                // The path's length is the two bytes before it, at 34.
                payload[35] = (byte)pathLength;
            }

            (string token, _) = tokens.Create("upload", payload, TimeSpan.FromMinutes(5));

            Assert.False(new UploadLinks(tokens, Unconfigured).TryRead(token, out _));
        }

        private static string TokenOf(UploadLink link)
        {
            return link.Url[$"/{UploadLinks.Route}/".Length..];
        }

        private static async Task<UploadLink> CreateLinkAsync(HttpClient client, CreateUploadLinkRequest request)
        {
            HttpResponseMessage response = await client.PostAsJsonAsync("/api/shelves/docs/upload-link", request);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<UploadLink>())!;
        }

        private Task<HttpResponseMessage> PutAsync(UploadLink link, string text)
        {
            return _host.AnonymousClient.PutAsync(link.Url, new ByteArrayContent(Encoding.UTF8.GetBytes(text)));
        }
    }
}
