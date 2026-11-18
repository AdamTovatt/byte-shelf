using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfClient.Encryption;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.Storage
{
    /// <summary>Links that let anyone holding one store one file without a token, acting as whoever made the link.</summary>
    public sealed class UploadLinksTests : IAsyncLifetime
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
            Assert.InRange(link.ExpiresAt, before + UploadLink.Lifetime.Default, DateTimeOffset.UtcNow + UploadLink.Lifetime.Default);
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
        public async Task Link_DescriptionOfTheLongestKind_StoresItWhole()
        {
            // The longest description, three bytes of UTF-8 a character: more than a URL could carry, which the link's row need not.
            string description = new string('€', FileDescription.MaxLength);
            UploadLink link = await CreateLinkAsync(_host.AdminClient, new CreateUploadLinkRequest { Path = "/a.txt", Description = description });

            (await PutAsync(link, "a")).EnsureSuccessStatusCode();

            ShelfFile file = (await _host.AdminClient.GetFromJsonAsync<ShelfFile>("/api/shelves/docs/files?path=/a.txt"))!;
            Assert.Equal(description, file.Description);
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
        public async Task Link_FolderEncryptedSinceItWasMade_Returns409AndStoresNothing()
        {
            (await _host.AdminClient.PostAsJsonAsync("/api/shelves/docs/directories", new CreateDirectoryRequest { Path = "/secret" })).EnsureSuccessStatusCode();
            UploadLink link = await CreateLinkAsync(_host.AdminClient, new CreateUploadLinkRequest { Path = "/secret/a.txt" });
            string check = await FolderPassword.CreateCheckAsync("pw", CancellationToken.None, workFactor: 10);
            (await _host.AdminClient.PutAsJsonAsync("/api/shelves/docs/directories/encryption?path=/secret", new SetFolderEncryptionRequest { Check = check })).EnsureSuccessStatusCode();

            HttpResponseMessage response = await PutAsync(link, "plain");

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _host.AdminClient.GetAsync("/api/shelves/docs/content?path=/secret/a.txt")).StatusCode);
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
            await AssertNothingAtAsync("docs", "/a.txt");
        }

        [Fact]
        public async Task Link_MadeByAnAdminWhoIsNoLongerOne_Returns404AndStoresNothing()
        {
            (await _host.AdminClient.PutAsJsonAsync($"/api/users/{_host.MemberId}/roles", new { roles = new[] { UserRoles.Admin } })).EnsureSuccessStatusCode();
            UploadLink link = await CreateLinkAsync(_host.MemberClient, new CreateUploadLinkRequest { Path = "/a.txt" });
            (await _host.AdminClient.PutAsJsonAsync($"/api/users/{_host.MemberId}/roles", new { roles = Array.Empty<string>() })).EnsureSuccessStatusCode();

            HttpResponseMessage response = await PutAsync(link, "a");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            await AssertNothingAtAsync("docs", "/a.txt");
        }

        [Fact]
        public async Task Link_MakerMayOnlyReadNow_Returns403()
        {
            await _host.GrantMemberAsync("docs", ShelfAccessPermission.ReadWrite);
            UploadLink link = await CreateLinkAsync(_host.MemberClient, new CreateUploadLinkRequest { Path = "/a.txt" });
            await _host.GrantMemberAsync("docs", ShelfAccessPermission.Read);

            HttpResponseMessage response = await PutAsync(link, "a");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            await AssertNothingAtAsync("docs", "/a.txt");
        }

        [Fact]
        public async Task Link_MakersAccessTakenAway_Returns404()
        {
            await _host.GrantMemberAsync("docs", ShelfAccessPermission.ReadWrite);
            UploadLink link = await CreateLinkAsync(_host.MemberClient, new CreateUploadLinkRequest { Path = "/a.txt" });
            (await _host.AdminClient.DeleteAsync($"/api/shelves/docs/access/{_host.MemberId}")).EnsureSuccessStatusCode();

            HttpResponseMessage response = await PutAsync(link, "a");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            await AssertNothingAtAsync("docs", "/a.txt");
        }

        [Fact]
        public async Task Link_OverQuota_Returns507()
        {
            (await _host.AdminClient.PostAsJsonAsync("/api/shelves", new CreateShelfRequest { Name = "small", QuotaBytes = 4 })).EnsureSuccessStatusCode();
            HttpResponseMessage created = await _host.AdminClient.PostAsJsonAsync("/api/shelves/small/upload-link", new CreateUploadLinkRequest { Path = "/a.txt" });
            UploadLink link = (await created.Content.ReadFromJsonAsync<UploadLink>())!;

            HttpResponseMessage response = await PutAsync(link, "five!");

            Assert.Equal(HttpStatusCode.InsufficientStorage, response.StatusCode);
            await AssertNothingAtAsync("small", "/a.txt");
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
        public async Task Link_Revoked_Returns404AndStoresNothing()
        {
            UploadLink link = await CreateLinkAsync(_host.AdminClient, new CreateUploadLinkRequest { Path = "/a.txt" });
            (await _host.AdminClient.DeleteAsync($"/api/shelves/docs/links/{link.Id}")).EnsureSuccessStatusCode();

            HttpResponseMessage response = await PutAsync(link, "a");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            await AssertNothingAtAsync("docs", "/a.txt");
        }

        [Fact]
        public async Task Link_RevokedWhileItsBodyArrives_StillStoresTheFile()
        {
            UploadLink link = await CreateLinkAsync(_host.AdminClient, new CreateUploadLinkRequest { Path = "/a.bin" });
            PausedContent content = new PausedContent(new byte[1024 * 1024], new byte[] { 1, 2, 3 });

            Task<HttpResponseMessage> sending = _host.AnonymousClient.PutAsync(link.Url, content);
            await content.FirstPartTaken.WaitAsync(TimeSpan.FromSeconds(30));
            (await _host.AdminClient.DeleteAsync($"/api/shelves/docs/links/{link.Id}")).EnsureSuccessStatusCode();
            content.Resume.SetResult();
            HttpResponseMessage response = await sending.WaitAsync(TimeSpan.FromSeconds(30));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal((1024L * 1024) + 3, (await response.Content.ReadFromJsonAsync<ShelfFile>())!.SizeBytes);
        }

        private async Task AssertNothingAtAsync(string shelf, string path)
        {
            Assert.Equal(HttpStatusCode.NotFound, (await _host.AdminClient.GetAsync($"/api/shelves/{shelf}/files?path={Uri.EscapeDataString(path)}")).StatusCode);
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

        /// <summary>
        /// A body sent in two parts, pausing between them until <see cref="Resume"/>. The first part is far
        /// past what the test server's request pipe holds unread, so it is taken only while the server
        /// reads the body: <see cref="FirstPartTaken"/> means the request is past the link's check.
        /// </summary>
        private sealed class PausedContent : HttpContent
        {
            private readonly byte[] _first;
            private readonly byte[] _rest;
            private readonly TaskCompletionSource _firstPartTaken = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            public PausedContent(byte[] first, byte[] rest)
            {
                _first = first;
                _rest = rest;
            }

            public Task FirstPartTaken => _firstPartTaken.Task;

            public TaskCompletionSource Resume { get; } = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            {
                await stream.WriteAsync(_first);
                await stream.FlushAsync();
                _firstPartTaken.SetResult();
                await Resume.Task;
                await stream.WriteAsync(_rest);
            }

            protected override bool TryComputeLength(out long length)
            {
                length = _first.Length + _rest.Length;
                return true;
            }
        }
    }
}
