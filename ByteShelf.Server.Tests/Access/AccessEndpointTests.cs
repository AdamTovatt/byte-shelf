using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.Access
{
    /// <summary>
    /// Who may do what over HTTP: the key scheme, the 404 for no access and 403 for too little, and the
    /// keys that cannot reach anything but their own shelf.
    /// </summary>
    public class AccessEndpointTests : IAsyncLifetime
    {
        private ServerTestHost _host = null!;

        public async Task InitializeAsync()
        {
            _host = await ServerTestHost.StartAsync();
            foreach (string name in new[] { "docs", "other" })
            {
                HttpResponseMessage created = await _host.AdminClient.PostAsJsonAsync("/api/shelves", new CreateShelfRequest { Name = name });
                created.EnsureSuccessStatusCode();
            }

            (await PutAsync(_host.AdminClient, "/existing.txt", "old")).EnsureSuccessStatusCode();
        }

        public async Task DisposeAsync()
        {
            await _host.DisposeAsync();
        }

        [Fact]
        public async Task PostKeys_ThenUseTheSecret_ListsTheShelf()
        {
            using HttpClient key = await CreateKeyClientAsync("docs", ShelfKeyPermission.Read);

            HttpResponseMessage response = await key.GetAsync("/api/shelves/docs/entries");

            Assert.Equal("/existing.txt", Assert.Single((await response.Content.ReadFromJsonAsync<DirectoryListing>())!.Files).Path);
        }

        [Fact]
        public async Task KeyAsBearerToken_ListsTheShelf()
        {
            string secret = (await CreateKeyAsync("docs", ShelfKeyPermission.Read)).Secret;
            using HttpClient client = _host.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);

            HttpResponseMessage response = await client.GetAsync("/api/shelves/docs/entries");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task KeyAsHeaderAndBearerTokenBoth_Returns401()
        {
            string secret = (await CreateKeyAsync("docs", ShelfKeyPermission.Read)).Secret;
            using HttpClient client = _host.CreateKeyClient(secret);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);

            HttpResponseMessage response = await client.GetAsync("/api/shelves/docs/entries");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task ReadKey_PutContent_Returns403()
        {
            using HttpClient key = await CreateKeyClientAsync("docs", ShelfKeyPermission.Read);

            HttpResponseMessage response = await PutAsync(key, "/new.txt", "new");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(ErrorCodes.PermissionDenied, (await response.Content.ReadFromJsonAsync<ErrorResponse>())?.Code);
        }

        [Fact]
        public async Task AddOnlyKey_PutNewFile_StoresIt()
        {
            using HttpClient key = await CreateKeyClientAsync("docs", ShelfKeyPermission.AddOnly);

            HttpResponseMessage response = await PutAsync(key, "/backups/new.txt", "new");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Theory]
        [InlineData(ShelfKeyPermission.Read)]
        [InlineData(ShelfKeyPermission.AddOnly)]
        [InlineData(ShelfKeyPermission.ReadWrite)]
        public async Task AnyKey_CreateDownloadLink_ServesTheFileWithoutSigningIn(ShelfKeyPermission permission)
        {
            using HttpClient key = await CreateKeyClientAsync("docs", permission);

            HttpResponseMessage created = await key.PostAsync("/api/shelves/docs/download-link?path=/existing.txt", null);
            created.EnsureSuccessStatusCode();
            DownloadLink link = (await created.Content.ReadFromJsonAsync<DownloadLink>())!;

            Assert.Equal("old", await _host.AnonymousClient.GetStringAsync(link.Url));
        }

        [Fact]
        public async Task AddOnlyKey_PutWithOverwrite_Returns403AndKeepsTheFile()
        {
            using HttpClient key = await CreateKeyClientAsync("docs", ShelfKeyPermission.AddOnly);

            HttpResponseMessage response = await PutAsync(key, "/existing.txt", "wiped", overwrite: true);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("old", await _host.AdminClient.GetStringAsync("/api/shelves/docs/content?path=/existing.txt"));
        }

        [Fact]
        public async Task AddOnlyKey_StartUploadWithOverwrite_Returns403()
        {
            using HttpClient key = await CreateKeyClientAsync("docs", ShelfKeyPermission.AddOnly);

            HttpResponseMessage response = await key.PostAsJsonAsync(
                "/api/shelves/docs/uploads", new StartUploadRequest { Path = "/existing.txt", SizeBytes = 5, Overwrite = true });

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task AddOnlyKey_CompleteAnOverwritingUploadSomeoneElseStarted_Returns403()
        {
            HttpResponseMessage started = await _host.AdminClient.PostAsJsonAsync(
                "/api/shelves/docs/uploads", new StartUploadRequest { Path = "/existing.txt", SizeBytes = 3, Overwrite = true });
            UploadSession upload = (await started.Content.ReadFromJsonAsync<UploadSession>())!;
            (await _host.AdminClient.PutAsync($"/api/shelves/docs/uploads/{upload.Id}?offset=0", new ByteArrayContent(Encoding.UTF8.GetBytes("new")))).EnsureSuccessStatusCode();
            using HttpClient key = await CreateKeyClientAsync("docs", ShelfKeyPermission.AddOnly);

            HttpResponseMessage response = await key.PostAsync($"/api/shelves/docs/uploads/{upload.Id}/complete", null);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("old", await _host.AdminClient.GetStringAsync("/api/shelves/docs/content?path=/existing.txt"));
        }

        [Fact]
        public async Task AddOnlyKey_DeleteFile_Returns403()
        {
            using HttpClient key = await CreateKeyClientAsync("docs", ShelfKeyPermission.AddOnly);

            HttpResponseMessage response = await key.DeleteAsync("/api/shelves/docs/files?path=/existing.txt");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task ReadWriteKey_ManageKeys_Returns403()
        {
            using HttpClient key = await CreateKeyClientAsync("docs", ShelfKeyPermission.ReadWrite);

            HttpResponseMessage response = await key.GetAsync("/api/shelves/docs/keys");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task Key_AnotherShelf_Returns404()
        {
            using HttpClient key = await CreateKeyClientAsync("docs", ShelfKeyPermission.ReadWrite);

            HttpResponseMessage response = await key.GetAsync("/api/shelves/other/entries");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task Key_ListShelves_ListsOnlyItsOwn()
        {
            using HttpClient key = await CreateKeyClientAsync("docs", ShelfKeyPermission.Read);

            IReadOnlyList<ShelfInfo>? shelves = await key.GetFromJsonAsync<IReadOnlyList<ShelfInfo>>("/api/shelves");

            Assert.Equal("docs", Assert.Single(shelves!).Name);
        }

        [Fact]
        public async Task Key_UserEndpoint_Returns401()
        {
            using HttpClient key = await CreateKeyClientAsync("docs", ShelfKeyPermission.ReadWrite);

            HttpResponseMessage response = await key.GetAsync("/api/users/me");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task KeyAsBearerToken_UserEndpoint_Returns401()
        {
            string secret = (await CreateKeyAsync("docs", ShelfKeyPermission.ReadWrite)).Secret;
            using HttpClient client = _host.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);

            HttpResponseMessage response = await client.GetAsync("/api/users/me");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task UnknownKey_Returns401()
        {
            using HttpClient key = _host.CreateKeyClient(ShelfKeys.SecretPrefix + "not-a-key");

            HttpResponseMessage response = await key.GetAsync("/api/shelves/docs/entries");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task UnknownKeyAlongsideAnAdminToken_Returns401()
        {
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "/api/shelves/docs/entries");
            request.Headers.Add(ShelfKeys.HeaderName, ShelfKeys.SecretPrefix + "not-a-key");

            HttpResponseMessage response = await _host.AdminClient.SendAsync(request);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task DeleteKeys_ThenUseTheSecret_Returns401()
        {
            CreatedShelfKey created = await CreateKeyAsync("docs", ShelfKeyPermission.Read);
            using HttpClient key = _host.CreateKeyClient(created.Secret);

            (await _host.AdminClient.DeleteAsync($"/api/shelves/docs/keys/{created.Key.Id}")).EnsureSuccessStatusCode();
            HttpResponseMessage response = await key.GetAsync("/api/shelves/docs/entries");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task ValidReadKeyAlongsideAnAdminToken_PutContent_Returns403()
        {
            CreatedShelfKey created = await CreateKeyAsync("docs", ShelfKeyPermission.Read);

            HttpResponseMessage response = await _host.AdminClient.SendAsync(WithKeyHeader(PutRequest("/new.txt", "new"), created.Secret));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task Key_CreateShelf_Returns401()
        {
            using HttpClient key = await CreateKeyClientAsync("docs", ShelfKeyPermission.ReadWrite);

            HttpResponseMessage response = await key.PostAsJsonAsync("/api/shelves", new CreateShelfRequest { Name = "mine" });

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task ReadWriteMember_UpdateShelf_Returns403()
        {
            await GrantMemberAsync("docs", ShelfAccessPermission.ReadWrite);

            HttpResponseMessage response = await _host.MemberClient.PutAsJsonAsync("/api/shelves/docs", new UpdateShelfRequest { Name = "renamed" });

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task ReadWriteMember_DeleteShelf_Returns403()
        {
            await GrantMemberAsync("docs", ShelfAccessPermission.ReadWrite);

            HttpResponseMessage response = await _host.MemberClient.DeleteAsync("/api/shelves/docs");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task AddOnlyKey_StartSendAndCompleteANewUpload_StoresTheFile()
        {
            using HttpClient key = await CreateKeyClientAsync("docs", ShelfKeyPermission.AddOnly);
            UploadSession upload = await StartUploadAsync(key, "/new.txt", "new");

            HttpResponseMessage response = await key.PostAsync($"/api/shelves/docs/uploads/{upload.Id}/complete", null);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("new", await _host.AdminClient.GetStringAsync("/api/shelves/docs/content?path=/new.txt"));
        }

        [Fact]
        public async Task AddOnlyKey_AppendToAnotherKeysUpload_Returns403()
        {
            using HttpClient starter = await CreateKeyClientAsync("docs", ShelfKeyPermission.AddOnly);
            UploadSession upload = await StartUploadAsync(starter, "/new.txt", sizeBytes: 3);
            using HttpClient other = await CreateKeyClientAsync("docs", ShelfKeyPermission.AddOnly);

            HttpResponseMessage response = await other.PutAsync($"/api/shelves/docs/uploads/{upload.Id}?offset=0", new ByteArrayContent(Encoding.UTF8.GetBytes("bad")));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task AddOnlyKey_AbortSomeoneElsesUpload_Returns403AndKeepsIt()
        {
            UploadSession upload = await StartUploadAsync(_host.AdminClient, "/new.txt", sizeBytes: 3);
            using HttpClient key = await CreateKeyClientAsync("docs", ShelfKeyPermission.AddOnly);

            HttpResponseMessage response = await key.DeleteAsync($"/api/shelves/docs/uploads/{upload.Id}");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await _host.AdminClient.GetAsync($"/api/shelves/docs/uploads/{upload.Id}")).StatusCode);
        }

        [Fact]
        public async Task ReadWriteKey_AbortSomeoneElsesUpload_DropsIt()
        {
            UploadSession upload = await StartUploadAsync(_host.AdminClient, "/new.txt", sizeBytes: 3);
            using HttpClient key = await CreateKeyClientAsync("docs", ShelfKeyPermission.ReadWrite);

            HttpResponseMessage response = await key.DeleteAsync($"/api/shelves/docs/uploads/{upload.Id}");

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        [Fact]
        public async Task ReadMember_ListEntries_Returns200()
        {
            await GrantMemberAsync("docs", ShelfAccessPermission.Read);

            HttpResponseMessage response = await _host.MemberClient.GetAsync("/api/shelves/docs/entries");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task ReadMember_PutContent_Returns403()
        {
            await GrantMemberAsync("docs", ShelfAccessPermission.Read);

            HttpResponseMessage response = await PutAsync(_host.MemberClient, "/new.txt", "new");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task Member_ListShelves_ListsOnlyTheShelvesTheyHaveAccessTo()
        {
            await GrantMemberAsync("other", ShelfAccessPermission.ReadWrite);

            IReadOnlyList<ShelfInfo>? shelves = await _host.MemberClient.GetFromJsonAsync<IReadOnlyList<ShelfInfo>>("/api/shelves");

            Assert.Equal("other", Assert.Single(shelves!).Name);
        }

        [Fact]
        public async Task Member_ManageAccess_Returns403EvenWithReadWrite()
        {
            await GrantMemberAsync("docs", ShelfAccessPermission.ReadWrite);

            HttpResponseMessage response = await _host.MemberClient.GetAsync("/api/shelves/docs/access");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task DeleteUser_Member_TheirStillValidTokenNoLongerReachesTheShelf()
        {
            await GrantMemberAsync("docs", ShelfAccessPermission.Read);
            (await _host.AdminClient.DeleteAsync($"/api/users/{_host.MemberId}")).EnsureSuccessStatusCode();

            HttpResponseMessage response = await _host.MemberClient.GetAsync("/api/shelves/docs/entries");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task DownloadLinkMadeWithAKey_KeyDeleted_StillServesTheFile()
        {
            CreatedShelfKey created = await CreateKeyAsync("docs", ShelfKeyPermission.Read);
            using HttpClient key = _host.CreateKeyClient(created.Secret);
            DownloadLink link = await CreateLinkAsync(key, "/existing.txt");

            (await _host.AdminClient.DeleteAsync($"/api/shelves/docs/keys/{created.Key.Id}")).EnsureSuccessStatusCode();

            Assert.Equal("old", await _host.AnonymousClient.GetStringAsync(link.Url));
        }

        [Fact]
        public async Task DownloadLinkMadeByAMember_GrantTakenAway_StillServesTheFile()
        {
            await GrantMemberAsync("docs", ShelfAccessPermission.Read);
            DownloadLink link = await CreateLinkAsync(_host.MemberClient, "/existing.txt");

            (await _host.AdminClient.DeleteAsync($"/api/shelves/docs/access/{_host.MemberId}")).EnsureSuccessStatusCode();

            Assert.Equal("old", await _host.AnonymousClient.GetStringAsync(link.Url));
        }

        private static async Task<DownloadLink> CreateLinkAsync(HttpClient client, string path)
        {
            HttpResponseMessage created = await client.PostAsync($"/api/shelves/docs/download-link?path={Uri.EscapeDataString(path)}", null);
            created.EnsureSuccessStatusCode();
            return (await created.Content.ReadFromJsonAsync<DownloadLink>())!;
        }

        private async Task GrantMemberAsync(string shelf, ShelfAccessPermission permission)
        {
            HttpResponseMessage response = await _host.AdminClient.PutAsJsonAsync(
                $"/api/shelves/{shelf}/access/{_host.MemberId}", new SetShelfAccessRequest { Permission = permission });
            response.EnsureSuccessStatusCode();
        }

        /// <summary>Starts an upload on docs, and sends <paramref name="text"/> as its one chunk when given.</summary>
        private static async Task<UploadSession> StartUploadAsync(HttpClient client, string path, string? text = null, long? sizeBytes = null)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
            HttpResponseMessage started = await client.PostAsJsonAsync(
                "/api/shelves/docs/uploads", new StartUploadRequest { Path = path, SizeBytes = sizeBytes ?? bytes.Length });
            started.EnsureSuccessStatusCode();
            UploadSession upload = (await started.Content.ReadFromJsonAsync<UploadSession>())!;

            if (text != null)
            {
                (await client.PutAsync($"/api/shelves/docs/uploads/{upload.Id}?offset=0", new ByteArrayContent(bytes))).EnsureSuccessStatusCode();
            }

            return upload;
        }

        private static HttpRequestMessage WithKeyHeader(HttpRequestMessage request, string secret)
        {
            request.Headers.Add(ShelfKeys.HeaderName, secret);
            return request;
        }

        private async Task<CreatedShelfKey> CreateKeyAsync(string shelf, ShelfKeyPermission permission)
        {
            HttpResponseMessage response = await _host.AdminClient.PostAsJsonAsync(
                $"/api/shelves/{shelf}/keys", new CreateShelfKeyRequest { Name = "test program", Permission = permission });
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<CreatedShelfKey>())!;
        }

        private async Task<HttpClient> CreateKeyClientAsync(string shelf, ShelfKeyPermission permission)
        {
            return _host.CreateKeyClient((await CreateKeyAsync(shelf, permission)).Secret);
        }

        private static Task<HttpResponseMessage> PutAsync(HttpClient client, string path, string text, bool overwrite = false)
        {
            return client.SendAsync(PutRequest(path, text, overwrite));
        }

        private static HttpRequestMessage PutRequest(string path, string text, bool overwrite = false)
        {
            ByteArrayContent content = new ByteArrayContent(Encoding.UTF8.GetBytes(text));
            content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
            return new HttpRequestMessage(HttpMethod.Put, $"/api/shelves/docs/content?path={Uri.EscapeDataString(path)}&overwrite={overwrite}") { Content = content };
        }
    }
}
