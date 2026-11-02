using System.Net;
using System.Text;
using System.Text.Json;
using ByteShelf.Server.Email;
using ByteShelf.Server.Email.Models;
using ByteShelf.Server.Email.Services;
using ByteShelf.Server.Tests.TestUtilities;

namespace ByteShelf.Server.Tests.Email
{
    /// <summary>What goes to Postmark, and what its answers become.</summary>
    public sealed class PostmarkEmailSenderTests
    {
        private static readonly PostmarkEmail Settings = new PostmarkEmail("server-token", new MailSender("shelf@example.com", "Shelf"));

        private static readonly EmailMessage ResetEmail = new EmailMessage
        {
            To = "user@example.com",
            Subject = "Reset your password",
            HtmlBody = "<p>Reset</p>",
            TextBody = "Reset",
            Tag = "password-reset",
        };

        [Fact]
        public async Task SendAsync_PostsTheMessageWithTheTokenAndTrackingOff()
        {
            RecordingHandler handler = new RecordingHandler(HttpStatusCode.OK, """{"MessageID":"abc-123","ErrorCode":0,"Message":"OK"}""");

            EmailSendResult result = await Sender(handler).SendAsync(ResetEmail);

            Assert.True(result.IsSuccess);
            Assert.Equal("abc-123", result.MessageId);
            Assert.Equal(HttpMethod.Post, handler.Method);
            Assert.Equal(new Uri("https://api.postmarkapp.com/email"), handler.Uri);
            Assert.Equal("server-token", handler.Token);

            using JsonDocument body = JsonDocument.Parse(handler.Body!);
            JsonElement root = body.RootElement;
            Assert.Equal("Shelf <shelf@example.com>", root.GetProperty("From").GetString());
            Assert.Equal("user@example.com", root.GetProperty("To").GetString());
            Assert.Equal("Reset your password", root.GetProperty("Subject").GetString());
            Assert.Equal("<p>Reset</p>", root.GetProperty("HtmlBody").GetString());
            Assert.Equal("Reset", root.GetProperty("TextBody").GetString());
            Assert.Equal("password-reset", root.GetProperty("Tag").GetString());

            // Sent, not left out: an absent field is the dashboard's setting rather than ours.
            Assert.False(root.GetProperty("TrackOpens").GetBoolean());
            Assert.Equal("None", root.GetProperty("TrackLinks").GetString());
        }

        [Fact]
        public async Task SendAsync_PostmarkRefuses_FailsWithItsMessage()
        {
            RecordingHandler handler = new RecordingHandler((HttpStatusCode)422, """{"ErrorCode":300,"Message":"Invalid 'To' address"}""");

            EmailSendResult result = await Sender(handler).SendAsync(ResetEmail);

            Assert.False(result.IsSuccess);
            Assert.Equal("Invalid 'To' address", result.ErrorMessage);
        }

        [Fact]
        public async Task SendAsync_PostmarkUnreachable_FailsWithoutThrowing()
        {
            RecordingHandler handler = new RecordingHandler(new HttpRequestException("No route to host"));

            EmailSendResult result = await Sender(handler).SendAsync(ResetEmail);

            Assert.False(result.IsSuccess);
            Assert.Equal("No route to host", result.ErrorMessage);
        }

        [Theory]
        [InlineData("<html>Bad gateway</html>", "Failed to parse Postmark's response.")]
        [InlineData("null", "Postmark returned an empty response.")]
        public async Task SendAsync_AnswerNotAMessage_FailsWithoutThrowing(string answer, string error)
        {
            EmailSendResult result = await Sender(new RecordingHandler(HttpStatusCode.OK, answer)).SendAsync(ResetEmail);

            Assert.False(result.IsSuccess);
            Assert.Equal(error, result.ErrorMessage);
        }

        [Fact]
        public async Task SendAsync_TheClientTimesOut_FailsWithoutThrowing()
        {
            // What HttpClient throws when its own Timeout passes: a cancellation the caller did not ask for.
            RecordingHandler handler = new RecordingHandler(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout."));

            EmailSendResult result = await Sender(handler).SendAsync(ResetEmail);

            Assert.False(result.IsSuccess);
            Assert.Equal("Postmark did not answer in time.", result.ErrorMessage);
        }

        [Fact]
        public async Task SendAsync_TheCallerCancels_Throws()
        {
            using CancellationTokenSource cancel = new CancellationTokenSource();
            cancel.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Sender(new RecordingHandler(HttpStatusCode.OK, "{}")).SendAsync(ResetEmail, cancel.Token));
        }

        private static PostmarkEmailSender Sender(RecordingHandler handler)
        {
            return new PostmarkEmailSender(new ClientFactory(handler), Settings, new RecordingLogger<PostmarkEmailSender>());
        }

        private sealed class ClientFactory : IHttpClientFactory
        {
            private readonly HttpMessageHandler _handler;

            public ClientFactory(HttpMessageHandler handler)
            {
                _handler = handler;
            }

            public HttpClient CreateClient(string name)
            {
                Assert.Equal(PostmarkEmailSender.HttpClientName, name);
                return new HttpClient(_handler, disposeHandler: false) { BaseAddress = PostmarkEmailSender.ApiBaseAddress };
            }
        }

        private sealed class RecordingHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _status;
            private readonly string? _answer;
            private readonly Exception? _failure;

            public RecordingHandler(HttpStatusCode status, string answer)
            {
                _status = status;
                _answer = answer;
            }

            public RecordingHandler(Exception failure)
            {
                _failure = failure;
            }

            public HttpMethod? Method { get; private set; }

            public Uri? Uri { get; private set; }

            public string? Token { get; private set; }

            public string? Body { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Method = request.Method;
                Uri = request.RequestUri;
                Token = request.Headers.TryGetValues("X-Postmark-Server-Token", out IEnumerable<string>? values) ? values.Single() : null;
                Body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

                if (_failure != null)
                {
                    throw _failure;
                }

                return new HttpResponseMessage(_status) { Content = new StringContent(_answer!, Encoding.UTF8, "application/json") };
            }
        }
    }
}
