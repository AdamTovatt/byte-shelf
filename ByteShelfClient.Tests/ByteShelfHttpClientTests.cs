using System.Net;
using System.Text;
using ByteShelfClient.Http;
using ByteShelfCommon;

namespace ByteShelfClient.Tests
{
    public class ByteShelfHttpClientTests
    {
        private const string UserJson = "{\"id\":\"6f9619ff-8b86-d011-b42d-00c04fc964ff\",\"email\":\"a@b.test\",\"displayName\":\"A\",\"roles\":[]}";

        /// <summary>An access token that has already run out, so the first request renews it.</summary>
        private static readonly DateTimeOffset Expired = DateTimeOffset.UtcNow.AddMinutes(-1);

        [Fact]
        public async Task SendAsync_TokenRefusedWithAChallenge_RenewsAndSendsTheRequestAgain()
        {
            using ScriptedServer server = new ScriptedServer(request => (request.Method.Method, request.RequestUri!.AbsolutePath, request.Headers.Authorization?.Parameter) switch
            {
                ("POST", "/api/auth/login", _) => ScriptedServer.Json(ScriptedServer.Tokens("first", "refresh-1")),
                ("POST", "/api/auth/refresh", _) => ScriptedServer.Json(ScriptedServer.Tokens("second", "refresh-2")),
                ("GET", "/api/users/me", "first") => Challenge(),
                ("GET", "/api/users/me", "second") => ScriptedServer.Json(UserJson),
                _ => new HttpResponseMessage(HttpStatusCode.InternalServerError),
            });
            using HttpClient http = server.CreateClient();
            using UserSession session = await UserSession.SignInAsync(http, "a@b.test", "password");

            UserInfo me = await new ByteShelfHttpClient(http, session).GetCurrentUserAsync();

            Assert.Equal("a@b.test", me.Email);
            Assert.Equal(new[] { "POST /api/auth/login", "GET /api/users/me first", "POST /api/auth/refresh", "GET /api/users/me second" }, server.Requests);
            Assert.Equal("refresh-2", session.Tokens.RefreshToken);
        }

        [Fact]
        public async Task CreateDownloadLinkAsync_ServerBehindAPathPrefix_AnswersTheLinkUnderThePrefix()
        {
            using ScriptedServer server = new ScriptedServer(_ => ScriptedServer.Json("{\"url\":\"/api/downloads/abc\",\"expiresAt\":\"2026-11-02T12:00:00+00:00\"}"));
            using HttpClient http = server.CreateClient();
            http.BaseAddress = new Uri("http://shelf.test/files/");

            DownloadLink link = await new ByteShelfHttpClient(http, new ShelfKeyCredentials("secret")).Shelf("docs").CreateDownloadLinkAsync("/a.txt", TimeSpan.FromHours(2));

            Assert.Equal("http://shelf.test/files/api/downloads/abc", link.Url);
            Assert.Equal("POST /files/api/shelves/docs/download-link?path=%2Fa.txt&expiresInMinutes=120", Assert.Single(server.Requests));
        }

        [Fact]
        public async Task SendAsync_TwoRequestsNeedRenewing_RenewsOnce()
        {
            TaskCompletionSource refreshArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource releaseRefresh = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using ScriptedServer server = new ScriptedServer(async request =>
            {
                switch (request.RequestUri!.AbsolutePath)
                {
                    case "/api/auth/login":
                        return ScriptedServer.Json(ScriptedServer.Tokens("first", "refresh-1", Expired));
                    case "/api/auth/refresh":
                        refreshArrived.SetResult();
                        await releaseRefresh.Task;
                        return ScriptedServer.Json(ScriptedServer.Tokens("second", "refresh-2"));
                    default:
                        return ScriptedServer.Json(UserJson);
                }
            });
            using HttpClient http = server.CreateClient();
            using UserSession session = await UserSession.SignInAsync(http, "a@b.test", "password");
            ByteShelfHttpClient client = new ByteShelfHttpClient(http, session);

            // The second request finds the stale token while the first holds the renewal, and waits for it.
            Task<UserInfo> first = client.GetCurrentUserAsync();
            await refreshArrived.Task;
            Task<UserInfo> second = client.GetCurrentUserAsync();
            releaseRefresh.SetResult();
            await Task.WhenAll(first, second);

            Assert.Single(server.Requests, request => request.StartsWith("POST /api/auth/refresh", StringComparison.Ordinal));
            Assert.Equal(2, server.Requests.Count(request => request == "GET /api/users/me second"));
        }

        [Fact]
        public async Task Resume_KeptAccessTokenStillGood_UsesItWithoutRenewing()
        {
            using ScriptedServer server = new ScriptedServer(_ => ScriptedServer.Json(UserJson));
            using HttpClient http = server.CreateClient();
            using UserSession session = UserSession.Resume(http, Tokens("kept", "refresh-1", DateTimeOffset.UtcNow.AddHours(1)));

            await new ByteShelfHttpClient(http, session).GetCurrentUserAsync();

            Assert.Equal(new[] { "GET /api/users/me kept" }, server.Requests);
        }

        [Fact]
        public async Task Resume_KeptAccessTokenRunOut_RenewsBeforeTheRequest()
        {
            using ScriptedServer server = new ScriptedServer(request => request.RequestUri!.AbsolutePath switch
            {
                "/api/auth/refresh" => ScriptedServer.Json(ScriptedServer.Tokens("second", "refresh-2")),
                _ => ScriptedServer.Json(UserJson),
            });
            using HttpClient http = server.CreateClient();
            using UserSession session = UserSession.Resume(http, Tokens("kept", "refresh-1", Expired));

            await new ByteShelfHttpClient(http, session).GetCurrentUserAsync();

            Assert.Equal(new[] { "POST /api/auth/refresh", "GET /api/users/me second" }, server.Requests);
        }

        [Fact]
        public void Resume_TokensWithoutARefreshToken_Throws()
        {
            using HttpClient http = new HttpClient();

            Assert.Throws<ArgumentException>(() => UserSession.Resume(http, Tokens("kept", " ", Expired)));
        }

        [Fact]
        public async Task SharedSession_AnotherProcessRenewedMeanwhile_TakesUpTheirTokens()
        {
            using ScriptedServer server = new ScriptedServer(_ => ScriptedServer.Json(UserJson));
            using HttpClient http = server.CreateClient();
            MemorySessionStore store = new MemorySessionStore(Tokens("first", "refresh-1", Expired));
            using UserSession session = await UserSession.OpenSharedAsync(http, store);
            store.Tokens = Tokens("theirs", "refresh-2", DateTimeOffset.UtcNow.AddHours(1));

            await new ByteShelfHttpClient(http, session).GetCurrentUserAsync();

            Assert.Equal(new[] { "GET /api/users/me theirs" }, server.Requests);
        }

        [Fact]
        public async Task SharedSession_Due_RenewsUnderTheLockAndSavesTheNewTokens()
        {
            using ScriptedServer server = new ScriptedServer(request => request.RequestUri!.AbsolutePath switch
            {
                "/api/auth/refresh" => ScriptedServer.Json(ScriptedServer.Tokens("second", "refresh-2")),
                _ => ScriptedServer.Json(UserJson),
            });
            using HttpClient http = server.CreateClient();
            MemorySessionStore store = new MemorySessionStore(Tokens("first", "refresh-1", Expired));
            using UserSession session = await UserSession.OpenSharedAsync(http, store);

            await new ByteShelfHttpClient(http, session).GetCurrentUserAsync();

            Assert.Equal(("refresh-2", 1, false), (store.Tokens!.RefreshToken, store.Locks, store.Locked));
        }

        [Fact]
        public async Task SharedSession_NoLongerKept_ThrowsUnauthenticated()
        {
            using ScriptedServer server = new ScriptedServer(_ => ScriptedServer.Json(UserJson));
            using HttpClient http = server.CreateClient();
            MemorySessionStore store = new MemorySessionStore(Tokens("first", "refresh-1", Expired));
            using UserSession session = await UserSession.OpenSharedAsync(http, store);
            store.Tokens = null;

            ByteShelfException exception = await Assert.ThrowsAsync<ByteShelfException>(() => new ByteShelfHttpClient(http, session).GetCurrentUserAsync());

            Assert.Equal(ErrorCodes.Unauthenticated, exception.Code);
            Assert.Empty(server.Requests);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task OpenReadAsync_ReadToTheEnd_ChecksTheBytesAgainstTheirSha256(bool matches)
        {
            byte[] content = Encoding.UTF8.GetBytes("the contents");
            string sha256 = matches ? Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(content)) : new string('0', 64);
            using ScriptedServer server = new ScriptedServer(_ =>
            {
                HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };
                response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue($"\"{sha256}\"");
                return response;
            });
            using HttpClient http = server.CreateClient();
            await using Stream stream = await new ByteShelfHttpClient(http, new ShelfKeyCredentials("bsk_secret")).Shelf("docs").OpenReadAsync("/a.txt");

            Task read = stream.CopyToAsync(Stream.Null);

            if (matches)
            {
                await read;
            }
            else
            {
                IOException exception = await Assert.ThrowsAsync<IOException>(() => read);
                Assert.Contains("SHA-256", exception.Message, StringComparison.Ordinal);
            }
        }

        [Fact]
        public async Task SendAsync_CancelledWhileRenewing_KeepsTheNewRefreshToken()
        {
            using CancellationTokenSource cancel = new CancellationTokenSource();
            using ScriptedServer server = new ScriptedServer(request => request.RequestUri!.AbsolutePath switch
            {
                "/api/auth/login" => ScriptedServer.Json(ScriptedServer.Tokens("first", "refresh-1", Expired)),
                "/api/auth/refresh" => CancelThenAnswer(cancel, ScriptedServer.Json(ScriptedServer.Tokens("second", "refresh-2"))),
                _ => ScriptedServer.Json(UserJson),
            });
            using HttpClient http = server.CreateClient();
            using UserSession session = await UserSession.SignInAsync(http, "a@b.test", "password");

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ByteShelfHttpClient(http, session).GetCurrentUserAsync(cancel.Token));

            Assert.Equal("refresh-2", session.Tokens.RefreshToken);
        }

        [Fact]
        public async Task SendAsync_RenewalFailsOnTheServer_ThrowsThatFailureAndKeepsTheSession()
        {
            using ScriptedServer server = new ScriptedServer(request => request.RequestUri!.AbsolutePath switch
            {
                "/api/auth/login" => ScriptedServer.Json(ScriptedServer.Tokens("first", "refresh-1", Expired)),
                "/api/auth/refresh" => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
                _ => Challenge(),
            });
            using HttpClient http = server.CreateClient();
            using UserSession session = await UserSession.SignInAsync(http, "a@b.test", "password");

            ByteShelfException exception = await Assert.ThrowsAsync<ByteShelfException>(() => new ByteShelfHttpClient(http, session).GetCurrentUserAsync());

            Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
            Assert.Equal("refresh-1", session.Tokens.RefreshToken);
            Assert.DoesNotContain(server.Requests, request => request.StartsWith("GET", StringComparison.Ordinal));
        }

        [Fact]
        public async Task UploadAsync_SingleRequestRefusedWithAChallenge_IsNotSentAgain()
        {
            using ScriptedServer server = new ScriptedServer(request => (request.Method.Method, request.RequestUri!.AbsolutePath) switch
            {
                ("POST", "/api/auth/login") => ScriptedServer.Json(ScriptedServer.Tokens("first", "refresh-1")),
                ("POST", "/api/auth/refresh") => ScriptedServer.Json(ScriptedServer.Tokens("second", "refresh-2")),
                _ => Challenge(),
            });
            using HttpClient http = server.CreateClient();
            using UserSession session = await UserSession.SignInAsync(http, "a@b.test", "password");
            IShelfClient shelf = new ByteShelfHttpClient(http, session).Shelf("docs");

            ByteShelfException exception = await Assert.ThrowsAsync<ByteShelfException>(
                () => shelf.UploadAsync("/a.txt", new MemoryStream(Encoding.UTF8.GetBytes("abc"))));

            Assert.Equal(ErrorCodes.Unauthenticated, exception.Code);
            Assert.Single(server.Requests, request => request.StartsWith("PUT", StringComparison.Ordinal));
        }

        [Fact]
        public async Task SendAsync_401WithoutAChallenge_IsNotRetried()
        {
            using ScriptedServer server = new ScriptedServer(request => request.RequestUri!.AbsolutePath switch
            {
                "/api/auth/login" => ScriptedServer.Json(ScriptedServer.Tokens("first", "refresh-1")),
                _ => ScriptedServer.Json("{\"message\":\"The current password is incorrect.\",\"code\":\"invalid_credentials\"}", HttpStatusCode.Unauthorized),
            });
            using HttpClient http = server.CreateClient();
            using UserSession session = await UserSession.SignInAsync(http, "a@b.test", "password");

            ByteShelfException exception = await Assert.ThrowsAsync<ByteShelfException>(
                () => new ByteShelfHttpClient(http, session).ChangePasswordAsync("wrong", "a-new-password"));

            Assert.Equal(ErrorCodes.InvalidCredentials, exception.Code);
            Assert.DoesNotContain(server.Requests, request => request.StartsWith("POST /api/auth/refresh", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task UploadAsync_AChunkFails_AbortsTheUploadAndThrowsTheFailure(bool abortTimesOut)
        {
            Guid uploadId = Guid.NewGuid();
            using ScriptedServer server = new ScriptedServer(request => (request.Method.Method, request.RequestUri!.PathAndQuery) switch
            {
                ("POST", "/api/shelves/docs/uploads") => ScriptedServer.Json(SessionJson(uploadId)),
                ("PUT", var uri) when uri.EndsWith("?offset=0", StringComparison.Ordinal) => ScriptedServer.Json(SessionJson(uploadId)),
                ("PUT", _) => ScriptedServer.Json("{\"message\":\"Disk full.\",\"code\":\"quota_exceeded\"}", HttpStatusCode.InsufficientStorage),
                ("DELETE", _) when abortTimesOut => throw new TaskCanceledException("The abort timed out."),
                ("DELETE", _) => new HttpResponseMessage(HttpStatusCode.NoContent),
                _ => new HttpResponseMessage(HttpStatusCode.InternalServerError),
            });
            using HttpClient http = server.CreateClient();
            IShelfClient shelf = new ByteShelfHttpClient(http, new ShelfKeyCredentials("bsk_secret"), new ByteShelfHttpClientOptions { SingleRequestMaxBytes = 4, ChunkBytes = 3 }).Shelf("docs");

            ByteShelfException exception = await Assert.ThrowsAsync<ByteShelfException>(
                () => shelf.UploadAsync("/a.txt", new MemoryStream(Encoding.UTF8.GetBytes("123456"))));

            Assert.Equal(ErrorCodes.QuotaExceeded, exception.Code);
            Assert.Equal($"DELETE /api/shelves/docs/uploads/{uploadId:D}", server.Requests[^1]);
        }

        [Fact]
        public async Task SendAsync_ErrorWithoutABody_TakesItsCodeFromTheStatus()
        {
            using ScriptedServer server = new ScriptedServer(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
            using HttpClient http = server.CreateClient();

            ByteShelfException exception = await Assert.ThrowsAsync<ByteShelfException>(
                () => new ByteShelfHttpClient(http, new ShelfKeyCredentials("bsk_secret")).Shelf("docs").GetInfoAsync());

            Assert.Equal((ErrorCodes.NotFound, HttpStatusCode.NotFound), (exception.Code, exception.StatusCode));
        }

        [Fact]
        public void Constructor_BaseAddressWithoutTrailingSlash_Throws()
        {
            using HttpClient http = new HttpClient { BaseAddress = new Uri("http://shelf.test/byteshelf") };

            Assert.Throws<ArgumentException>(() => new ByteShelfHttpClient(http, new ShelfKeyCredentials("bsk_secret")));
        }

        private static HttpResponseMessage CancelThenAnswer(CancellationTokenSource cancel, HttpResponseMessage answer)
        {
            cancel.Cancel();
            return answer;
        }

        private static SessionTokens Tokens(string token, string refreshToken, DateTimeOffset expiresAt)
        {
            return new SessionTokens { Token = token, ExpiresAt = expiresAt, RefreshToken = refreshToken };
        }

        private static HttpResponseMessage Challenge()
        {
            HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.Unauthorized);
            response.Headers.WwwAuthenticate.ParseAdd("Bearer error=\"invalid_token\"");
            return response;
        }

        private static string SessionJson(Guid uploadId)
        {
            return $"{{\"id\":\"{uploadId:D}\",\"shelfId\":\"{Guid.Empty:D}\",\"path\":\"/a.txt\",\"contentType\":\"application/octet-stream\",\"sizeBytes\":6,\"receivedBytes\":0,\"overwrite\":false,\"createdAt\":\"2026-01-01T00:00:00Z\",\"expiresAt\":\"2026-01-02T00:00:00Z\"}}";
        }
    }
}
