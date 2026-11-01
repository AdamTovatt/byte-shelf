using ByteShelf.Server.Email.Models;
using ByteShelf.Server.Email.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;

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
            RecordingLogger logger = new RecordingLogger();

            await new LoggingEmailSender(logger, EnvironmentNamed(Environments.Production)).SendAsync(ResetEmail);

            string logged = Assert.Single(logger.Messages);
            Assert.DoesNotContain(Token, logged, StringComparison.Ordinal);
            Assert.Contains("password-reset", logged, StringComparison.Ordinal);
            Assert.Contains("no mail provider", logged, StringComparison.Ordinal);
        }

        [Fact]
        public async Task SendAsync_InDevelopment_LogsTheLinkToFollow()
        {
            RecordingLogger logger = new RecordingLogger();

            await new LoggingEmailSender(logger, EnvironmentNamed(Environments.Development)).SendAsync(ResetEmail);

            Assert.Contains(Token, Assert.Single(logger.Messages), StringComparison.Ordinal);
        }

        private static IHostEnvironment EnvironmentNamed(string name)
        {
            return Mock.Of<IHostEnvironment>(environment => environment.EnvironmentName == name);
        }

        private sealed class RecordingLogger : ILogger<LoggingEmailSender>
        {
            public List<string> Messages { get; } = new List<string>();

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
                return null;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                Messages.Add(formatter(state, exception));
            }
        }
    }
}
