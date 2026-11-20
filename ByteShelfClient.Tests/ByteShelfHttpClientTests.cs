using System.Net;
using System.Net.Http.Headers;
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
            using ScriptedServer server = new ScriptedServer(_ => ScriptedServer.Json(DownloadLinkJson("/api/downloads/abc")));
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

        [Fact]
        public async Task SharedSession_SignOut_EndsTheKeptTokensUnderTheLockAndForgetsThem()
        {
            MemorySessionStore store = new MemorySessionStore(Tokens("first", "refresh-1", Expired));
            List<(string Body, bool Locked)> logouts = new List<(string, bool)>();
            using ScriptedServer server = new ScriptedServer(async request =>
            {
                logouts.Add((await request.Content!.ReadAsStringAsync(), store.Locked));
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            });
            using HttpClient http = server.CreateClient();
            using UserSession session = await UserSession.OpenSharedAsync(http, store);
            store.Tokens = Tokens("theirs", "refresh-2", DateTimeOffset.UtcNow.AddHours(1));

            await session.SignOutAsync();

            (string body, bool locked) = Assert.Single(logouts);
            Assert.Contains("refresh-2", body, StringComparison.Ordinal);
            Assert.True(locked);
            Assert.Null(store.Tokens);
            Assert.False(store.Locked);
        }

        [Fact]
        public async Task SignOut_ThenARequest_ThrowsUnauthenticatedWithoutSendingIt()
        {
            using ScriptedServer server = new ScriptedServer(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
            using HttpClient http = server.CreateClient();
            using UserSession session = UserSession.Resume(http, Tokens("first", "refresh-1", DateTimeOffset.UtcNow.AddHours(1)));

            await session.SignOutAsync();
            ByteShelfException exception = await Assert.ThrowsAsync<ByteShelfException>(() => new ByteShelfHttpClient(http, session).GetCurrentUserAsync());

            Assert.Equal(ErrorCodes.Unauthenticated, exception.Code);
            Assert.Equal(new[] { "POST /api/auth/logout" }, server.Requests);
        }

        [Fact]
        public async Task SharedSession_SignedOutElsewhere_SignsOutWithoutCallingTheServer()
        {
            using ScriptedServer server = new ScriptedServer(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
            using HttpClient http = server.CreateClient();
            MemorySessionStore store = new MemorySessionStore(Tokens("first", "refresh-1", Expired));
            using UserSession session = await UserSession.OpenSharedAsync(http, store);
            store.Tokens = null;

            await session.SignOutAsync();

            Assert.Empty(server.Requests);
            await Assert.ThrowsAsync<ByteShelfException>(() => new ByteShelfHttpClient(http, session).GetCurrentUserAsync());
        }

        [Fact]
        public async Task OpenReadAsync_BytesMatchTheirSha256_ReadsThemToTheEnd()
        {
            byte[] content = Encoding.UTF8.GetBytes("the contents");
            using ScriptedServer server = ServingWithSha256(content, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(content)));
            using HttpClient http = server.CreateClient();
            await using Stream stream = await new ByteShelfHttpClient(http, new ShelfKeyCredentials("bsk_secret")).Shelf("docs").OpenReadAsync("/a.txt");
            using MemoryStream read = new MemoryStream();

            await stream.CopyToAsync(read);

            Assert.Equal(content, read.ToArray());
        }

        [Fact]
        public async Task OpenReadAsync_BytesDoNotMatchTheirSha256_ThrowsAtTheEnd()
        {
            using ScriptedServer server = ServingWithSha256(Encoding.UTF8.GetBytes("the contents"), new string('0', 64));
            using HttpClient http = server.CreateClient();
            await using Stream stream = await new ByteShelfHttpClient(http, new ShelfKeyCredentials("bsk_secret")).Shelf("docs").OpenReadAsync("/a.txt");

            IOException exception = await Assert.ThrowsAsync<IOException>(() => stream.CopyToAsync(Stream.Null));

            Assert.Contains("SHA-256", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task OpenReadRangeAsync_PartWithExpectedSha256_SendsRangeAndIfRange()
        {
            string sha256 = new string('a', 64);
            HttpRequestMessage? sent = null;
            using ScriptedServer server = new ScriptedServer(request =>
            {
                sent = request;
                return PartOf(Encoding.UTF8.GetBytes("0123456789"), 7, 2, sha256);
            });
            using HttpClient http = server.CreateClient();

            await using ShelfFileRange range = await ShelfOf(http).OpenReadRangeAsync("/a.txt", 7, 2, sha256.ToUpperInvariant());

            Assert.Equal(("bytes=7-8", $"\"{sha256}\"", "identity"), (sent!.Headers.Range?.ToString(), sent.Headers.IfRange?.ToString(), sent.Headers.AcceptEncoding.ToString()));
        }

        [Fact]
        public async Task OpenReadRangeAsync_FromTheStartWithNoLength_SendsAPlainRead()
        {
            byte[] content = Encoding.UTF8.GetBytes("the contents");
            string sha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(content));
            HttpRequestMessage? sent = null;
            using ScriptedServer server = new ScriptedServer(request =>
            {
                sent = request;
                return Whole(content, sha256);
            });
            using HttpClient http = server.CreateClient();

            await using ShelfFileRange range = await ShelfOf(http).OpenReadRangeAsync("/a.txt", 0, expectedSha256: sha256);

            Assert.Equal((null, null), (sent!.Headers.Range, sent.Headers.IfRange));
            Assert.Equal((0L, 12L, 12L), (range.Offset, range.Length, range.TotalLength));
        }

        [Fact]
        public async Task OpenReadRangeAsync_RangeThatIsTheWholeFile_ChecksItsSha256AtTheEnd()
        {
            byte[] content = Encoding.UTF8.GetBytes("the contents");
            using ScriptedServer server = new ScriptedServer(_ => PartOf(content, 0, content.Length, new string('0', 64)));
            using HttpClient http = server.CreateClient();
            await using ShelfFileRange range = await ShelfOf(http).OpenReadRangeAsync("/a.txt", 0, content.Length);

            IOException exception = await Assert.ThrowsAsync<IOException>(() => range.Content.CopyToAsync(Stream.Null));

            Assert.Contains("SHA-256", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task OpenReadRangeAsync_PartOfTheFile_ReadsItUnchecked()
        {
            byte[] content = Encoding.UTF8.GetBytes("the contents");
            using ScriptedServer server = new ScriptedServer(_ => PartOf(content, 4, 8, new string('0', 64)));
            using HttpClient http = server.CreateClient();
            await using ShelfFileRange range = await ShelfOf(http).OpenReadRangeAsync("/a.txt", 4);
            using MemoryStream read = new MemoryStream();

            await range.Content.CopyToAsync(read);

            Assert.Equal("contents", Encoding.UTF8.GetString(read.ToArray()));
        }

        [Theory]
        [InlineData("whole file to a range", 4L, null)]
        [InlineData("no ETag", 4L, null)]
        [InlineData("an ETag that is not a SHA-256", 4L, null)]
        [InlineData("no Content-Range", 4L, null)]
        [InlineData("another start", 4L, null)]
        [InlineData("more than asked for", 4L, 2L)]
        [InlineData("whole file without a Content-Length", 0L, null)]
        public async Task OpenReadRangeAsync_AnswerNotTheRangeAskedFor_ThrowsInvalidResponse(string answer, long offset, long? length)
        {
            byte[] content = Encoding.UTF8.GetBytes("the contents");
            string sha256 = new string('0', 64);
            using ScriptedServer server = new ScriptedServer(_ =>
            {
                HttpResponseMessage response = answer switch
                {
                    "whole file to a range" or "whole file without a Content-Length" => Whole(content, sha256),
                    "another start" => PartOf(content, 3, 8, sha256),
                    _ => PartOf(content, 4, 8, sha256),
                };
                switch (answer)
                {
                    case "no ETag":
                        response.Headers.ETag = null;
                        break;
                    case "an ETag that is not a SHA-256":
                        response.Headers.ETag = new EntityTagHeaderValue("\"v1\"");
                        break;
                    case "no Content-Range":
                        response.Content.Headers.ContentRange = null;
                        break;
                    case "whole file without a Content-Length":
                        response.Content.Headers.ContentLength = null;
                        break;
                }

                return response;
            });
            using HttpClient http = server.CreateClient();

            ByteShelfException exception = await Assert.ThrowsAsync<ByteShelfException>(() => ShelfOf(http).OpenReadRangeAsync("/a.txt", offset, length));

            Assert.Equal(ByteShelfException.InvalidResponse, exception.Code);
        }

        [Theory]
        [InlineData(6, "")]
        [InlineData(3, "456")]
        public async Task UploadAsync_ChunkInterrupted_GoesOnFromWhatTheServerHolds(long heldAfterTheFailure, string resent)
        {
            Guid uploadId = Guid.NewGuid();
            int failures = 0;
            List<string> sent = new List<string>();
            using ScriptedServer server = new ScriptedServer(request =>
            {
                string uri = request.RequestUri!.PathAndQuery;
                switch (request.Method.Method)
                {
                    case "POST" when uri.EndsWith("/complete", StringComparison.Ordinal):
                        return ScriptedServer.Json(FileJson());
                    case "POST":
                        return ScriptedServer.Json(SessionJson(uploadId));
                    case "GET":
                        return ScriptedServer.Json(SessionJson(uploadId, heldAfterTheFailure));
                    case "PUT" when uri.EndsWith("?offset=3", StringComparison.Ordinal) && failures++ == 0:
                        throw new HttpRequestException("The connection was reset.");
                    case "PUT":
                        sent.Add(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                        return ScriptedServer.Json(SessionJson(uploadId));
                    default:
                        return new HttpResponseMessage(HttpStatusCode.InternalServerError);
                }
            });
            using HttpClient http = server.CreateClient();
            IShelfClient shelf = new ByteShelfHttpClient(http, new ShelfKeyCredentials("bsk_secret"), new ByteShelfHttpClientOptions { SingleRequestMaxBytes = 4, ChunkBytes = 3 }).Shelf("docs");

            ShelfFile stored = await shelf.UploadAsync("/a.txt", new MemoryStream(Encoding.UTF8.GetBytes("123456")));

            Assert.Equal("/a.txt", stored.Path);
            Assert.Equal(new[] { "123", resent }.Where(text => text.Length > 0), sent);
            Assert.Contains($"GET /api/shelves/docs/uploads/{uploadId:D}", server.Requests);
            Assert.DoesNotContain(server.Requests, request => request.StartsWith("DELETE", StringComparison.Ordinal));
        }

        [Fact]
        public async Task UploadAsync_ChunkKeepsFailing_AbortsAfterResumingAFewTimes()
        {
            Guid uploadId = Guid.NewGuid();
            using ScriptedServer server = new ScriptedServer(request => (request.Method.Method, request.RequestUri!.PathAndQuery) switch
            {
                ("POST", _) => ScriptedServer.Json(SessionJson(uploadId)),
                ("GET", _) => ScriptedServer.Json(SessionJson(uploadId, receivedBytes: 3)),
                ("PUT", var uri) when uri.EndsWith("?offset=0", StringComparison.Ordinal) => ScriptedServer.Json(SessionJson(uploadId)),
                ("PUT", _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
                ("DELETE", _) => new HttpResponseMessage(HttpStatusCode.NoContent),
                _ => new HttpResponseMessage(HttpStatusCode.InternalServerError),
            });
            using HttpClient http = server.CreateClient();
            IShelfClient shelf = new ByteShelfHttpClient(http, new ShelfKeyCredentials("bsk_secret"), new ByteShelfHttpClientOptions { SingleRequestMaxBytes = 4, ChunkBytes = 3 }).Shelf("docs");

            ByteShelfException exception = await Assert.ThrowsAsync<ByteShelfException>(
                () => shelf.UploadAsync("/a.txt", new MemoryStream(Encoding.UTF8.GetBytes("123456"))));

            Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
            Assert.Equal(4, server.Requests.Count(request => request.EndsWith("?offset=3", StringComparison.Ordinal)));
            Assert.Equal($"DELETE /api/shelves/docs/uploads/{uploadId:D}", server.Requests[^1]);
        }

        [Fact]
        public async Task UploadAsync_ChunkRefusedWithAChallenge_RenewsAndSendsTheChunkAgain()
        {
            Guid uploadId = Guid.NewGuid();
            using ScriptedServer server = new ScriptedServer(request => (request.Method.Method, request.RequestUri!.PathAndQuery, request.Headers.Authorization?.Parameter) switch
            {
                ("POST", "/api/auth/login", _) => ScriptedServer.Json(ScriptedServer.Tokens("first", "refresh-1")),
                ("POST", "/api/auth/refresh", _) => ScriptedServer.Json(ScriptedServer.Tokens("second", "refresh-2")),
                ("POST", var uri, _) when uri.EndsWith("/complete", StringComparison.Ordinal) => ScriptedServer.Json(FileJson()),
                ("POST", _, _) => ScriptedServer.Json(SessionJson(uploadId)),
                ("PUT", var uri, "first") when uri.EndsWith("?offset=3", StringComparison.Ordinal) => Challenge(),
                ("PUT", _, _) => ScriptedServer.Json(SessionJson(uploadId)),
                _ => new HttpResponseMessage(HttpStatusCode.InternalServerError),
            });
            using HttpClient http = server.CreateClient();
            using UserSession session = await UserSession.SignInAsync(http, "a@b.test", "password");
            IShelfClient shelf = new ByteShelfHttpClient(http, session, new ByteShelfHttpClientOptions { SingleRequestMaxBytes = 4, ChunkBytes = 3 }).Shelf("docs");

            await shelf.UploadAsync("/a.txt", new MemoryStream(Encoding.UTF8.GetBytes("123456")));

            string chunk = $"PUT /api/shelves/docs/uploads/{uploadId:D}?offset=3";
            Assert.Equal(
                new[] { $"{chunk} first", "POST /api/auth/refresh", $"{chunk} second", $"POST /api/shelves/docs/uploads/{uploadId:D}/complete second" },
                server.Requests.SkipWhile(request => !request.StartsWith(chunk, StringComparison.Ordinal)));
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
        public async Task SendAsync_SuccessWhoseBodyIsNotJson_ThrowsInvalidResponse()
        {
            using ScriptedServer server = new ScriptedServer(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html><body>Welcome to nginx!</body></html>", Encoding.UTF8, "text/html"),
            });
            using HttpClient http = server.CreateClient();

            ByteShelfException exception = await Assert.ThrowsAsync<ByteShelfException>(
                () => new ByteShelfHttpClient(http, new ShelfKeyCredentials("bsk_secret")).Shelf("docs").GetInfoAsync());

            Assert.Equal((ByteShelfException.InvalidResponse, HttpStatusCode.OK), (exception.Code, exception.StatusCode));
        }

        [Theory]
        [InlineData("[1, 2, 3]")]
        [InlineData("")]
        [InlineData("null")]
        public async Task SendAsync_SuccessWhoseJsonIsNotTheShapeAsked_ThrowsInvalidResponse(string body)
        {
            using ScriptedServer server = new ScriptedServer(_ => ScriptedServer.Json(body));
            using HttpClient http = server.CreateClient();

            ByteShelfException exception = await Assert.ThrowsAsync<ByteShelfException>(
                () => new ByteShelfHttpClient(http, new ShelfKeyCredentials("bsk_secret")).Shelf("docs").GetInfoAsync());

            Assert.Equal(ByteShelfException.InvalidResponse, exception.Code);
        }

        [Fact]
        public void Constructor_BaseAddressWithoutTrailingSlash_Throws()
        {
            using HttpClient http = new HttpClient { BaseAddress = new Uri("http://shelf.test/byteshelf") };

            Assert.Throws<ArgumentException>(() => new ByteShelfHttpClient(http, new ShelfKeyCredentials("bsk_secret")));
        }

        [Theory]
        [InlineData("https://files.example.com/byteshelf")]
        [InlineData("byteshelf/")]
        public void Constructor_LinkBaseAddressNotAbsoluteEndingInASlash_Throws(string linkBase)
        {
            using HttpClient http = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:5000/") };
            ByteShelfHttpClientOptions options = new ByteShelfHttpClientOptions { LinkBaseAddress = new Uri(linkBase, UriKind.RelativeOrAbsolute) };

            Assert.Throws<ArgumentException>(() => new ByteShelfHttpClient(http, new ShelfKeyCredentials("bsk_secret"), options));
        }

        [Fact]
        public async Task CreateDownloadLinkAsync_WithALinkBaseAddress_AnswersTheLinkOnIt()
        {
            using ScriptedServer server = new ScriptedServer(_ => ScriptedServer.Json(DownloadLinkJson("/api/downloads/abc")));
            using HttpClient http = server.CreateClient();
            ByteShelfHttpClientOptions options = new ByteShelfHttpClientOptions { LinkBaseAddress = new Uri("https://files.example.com/shelf/") };

            DownloadLink link = await new ByteShelfHttpClient(http, new ShelfKeyCredentials("secret"), options).Shelf("docs").CreateDownloadLinkAsync("/a.txt");

            Assert.Equal("https://files.example.com/shelf/api/downloads/abc", link.Url);
        }

        [Fact]
        public async Task CreateDownloadLinkAsync_ServerAnswersOnItsPublicUrl_KeepsThatLink()
        {
            using ScriptedServer server = new ScriptedServer(_ => ScriptedServer.Json(DownloadLinkJson("https://public.example.com/api/downloads/abc")));
            using HttpClient http = server.CreateClient();
            ByteShelfHttpClientOptions options = new ByteShelfHttpClientOptions { LinkBaseAddress = new Uri("https://files.example.com/shelf/") };

            DownloadLink link = await new ByteShelfHttpClient(http, new ShelfKeyCredentials("secret"), options).Shelf("docs").CreateDownloadLinkAsync("/a.txt");

            Assert.Equal("https://public.example.com/api/downloads/abc", link.Url);
        }

        private static HttpResponseMessage CancelThenAnswer(CancellationTokenSource cancel, HttpResponseMessage answer)
        {
            cancel.Cancel();
            return answer;
        }

        /// <summary>A download link as the server answers it, at <paramref name="url"/>.</summary>
        private static string DownloadLinkJson(string url)
        {
            return $"{{\"id\":\"{Guid.Empty:D}\",\"url\":\"{url}\",\"expiresAt\":\"2026-11-02T12:00:00+00:00\"}}";
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

        private static string SessionJson(Guid uploadId, long receivedBytes = 0)
        {
            return $"{{\"id\":\"{uploadId:D}\",\"shelfId\":\"{Guid.Empty:D}\",\"path\":\"/a.txt\",\"contentType\":\"application/octet-stream\",\"sizeBytes\":6,\"receivedBytes\":{receivedBytes},\"overwrite\":false,\"createdAt\":\"2026-01-01T00:00:00Z\",\"expiresAt\":\"2026-01-02T00:00:00Z\"}}";
        }

        /// <summary>What completing the six-byte upload of /a.txt answers.</summary>
        private static string FileJson()
        {
            return $"{{\"id\":\"{Guid.NewGuid():D}\",\"shelfId\":\"{Guid.Empty:D}\",\"directoryId\":\"{Guid.Empty:D}\",\"path\":\"/a.txt\",\"name\":\"a.txt\",\"contentType\":\"text/plain\",\"sizeBytes\":6,\"sha256\":\"{new string('0', 64)}\",\"createdAt\":\"2026-01-01T00:00:00Z\",\"updatedAt\":\"2026-01-01T00:00:00Z\",\"encrypted\":false}}";
        }

        private static IShelfClient ShelfOf(HttpClient http)
        {
            return new ByteShelfHttpClient(http, new ShelfKeyCredentials("bsk_secret")).Shelf("docs");
        }

        /// <summary>A 200 with all of <paramref name="content"/>, under <paramref name="sha256"/> as its ETag.</summary>
        private static HttpResponseMessage Whole(byte[] content, string sha256)
        {
            HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };
            response.Headers.ETag = new EntityTagHeaderValue($"\"{sha256}\"");
            return response;
        }

        /// <summary>A 206 with <paramref name="count"/> bytes of <paramref name="content"/> from <paramref name="from"/>, as the server sends one.</summary>
        private static HttpResponseMessage PartOf(byte[] content, int from, int count, string sha256)
        {
            HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(content, from, count) };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, from + count - 1, content.Length);
            response.Headers.ETag = new EntityTagHeaderValue($"\"{sha256}\"");
            return response;
        }

        private static ScriptedServer ServingWithSha256(byte[] content, string sha256)
        {
            return new ScriptedServer(_ => Whole(content, sha256));
        }
    }
}
