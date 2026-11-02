using ByteShelf.Server.Email.Models;
using ByteShelf.Server.Email.Services;
using ByteShelf.Server.Tests.TestUtilities;
using Microsoft.Extensions.Hosting;

namespace ByteShelf.Server.Tests.Email
{
    /// <summary>What the sender used while no mail provider is configured writes to the log.</summary>
    public sealed class LoggingEmailSenderTests
    {
        private const string Token = "reset-token-from-the-link";

        private static readonly EmailMessage ResetEmail = new EmailMessage
        {
            To = "user@example.com",
            Subject = "Reset your password",
            HtmlBody = $"<a href=\"https://shelf.test/reset-password?token={Token}\">Reset</a>",
            TextBody = $"Visit https://shelf.test/reset-password?token={Token}",
            Tag = "password-reset",
        };

        [Fact]
        public async Task SendAsync_OutsideDevelopment_LogsThatItWasNotSentWithoutTheLink()
        {
            RecordingLogger<LoggingEmailSender> logger = new RecordingLogger<LoggingEmailSender>();

            await new LoggingEmailSender(logger, HostEnvironments.Named(Environments.Production)).SendAsync(ResetEmail);

            string logged = Assert.Single(logger.Messages);
            Assert.DoesNotContain(Token, logged, StringComparison.Ordinal);
            Assert.Contains("password-reset", logged, StringComparison.Ordinal);
            Assert.Contains("no mail provider", logged, StringComparison.Ordinal);
        }

        [Fact]
        public async Task SendAsync_InDevelopment_LogsTheLinkToFollow()
        {
            RecordingLogger<LoggingEmailSender> logger = new RecordingLogger<LoggingEmailSender>();

            await new LoggingEmailSender(logger, HostEnvironments.Named(Environments.Development)).SendAsync(ResetEmail);

            Assert.Contains(Token, Assert.Single(logger.Messages), StringComparison.Ordinal);
        }
    }
}
