using ByteShelf.Server.Email.Models;
using ByteShelf.Server.Email.Services;
using ByteShelf.Server.Shared;
using ByteShelf.Server.Users.Services;

namespace ByteShelf.Server.Tests.Users
{
    public class EmailPasswordResetNotifierTests
    {
        [Fact]
        public async Task SendResetLinkAsync_SendsALinkToTheResetPageWithTheTokenEscaped()
        {
            RecordingSender sender = new RecordingSender(succeed: true);
            EmailPasswordResetNotifier notifier = new EmailPasswordResetNotifier(sender, new PublicAddress("https://shelf.example.com/"));

            await notifier.SendResetLinkAsync("user@example.com", "a+b/c=");

            EmailMessage message = Assert.Single(sender.Sent);
            Assert.Equal("user@example.com", message.To);
            Assert.Contains("https://shelf.example.com/reset-password?token=a%2Bb%2Fc%3D", message.TextBody, StringComparison.Ordinal);
            Assert.Contains("1 hour", message.HtmlBody, StringComparison.Ordinal);
        }

        [Fact]
        public async Task SendResetLinkAsync_SenderFails_Throws()
        {
            EmailPasswordResetNotifier notifier = new EmailPasswordResetNotifier(new RecordingSender(succeed: false), new PublicAddress("https://shelf.example.com"));

            await Assert.ThrowsAsync<InvalidOperationException>(() => notifier.SendResetLinkAsync("user@example.com", "token"));
        }

        [Theory]
        [InlineData("https://example.com/shelf", "https://example.com/shelf/reset-password?token=t")]
        [InlineData(null, PublicAddress.DevelopmentUrl + "reset-password?token=t")]
        public async Task SendResetLinkAsync_LinksBelowThePublicUrlsPath_OrOnDevelopmentsWithoutOne(string? appBaseUrl, string expected)
        {
            RecordingSender sender = new RecordingSender(succeed: true);

            await new EmailPasswordResetNotifier(sender, new PublicAddress(appBaseUrl)).SendResetLinkAsync("user@example.com", "t");

            Assert.EndsWith(expected, Assert.Single(sender.Sent).TextBody, StringComparison.Ordinal);
        }

        private sealed class RecordingSender : IEmailSender
        {
            private readonly bool _succeed;

            public RecordingSender(bool succeed)
            {
                _succeed = succeed;
            }

            public List<EmailMessage> Sent { get; } = new List<EmailMessage>();

            public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
            {
                Sent.Add(message);
                return Task.FromResult(new EmailSendResult { IsSuccess = _succeed, ErrorMessage = _succeed ? null : "refused" });
            }
        }
    }
}
