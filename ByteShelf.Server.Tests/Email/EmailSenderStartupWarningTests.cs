using ByteShelf.Server.Email;
using ByteShelf.Server.Email.Services;
using ByteShelf.Server.Tests.TestUtilities;
using Microsoft.Extensions.Hosting;

namespace ByteShelf.Server.Tests.Email
{
    /// <summary>The startup warning: the one sign that reset links are not being delivered.</summary>
    public sealed class EmailSenderStartupWarningTests
    {
        private static readonly MailSender From = new MailSender("shelf@example.com", "Shelf");

        [Fact]
        public async Task Start_DevMail_SaysWhereMailIsWritten()
        {
            string logged = Assert.Single(await WarningsAsync(new DevMailEmail("/nowhere/mail", From), Environments.Development));

            Assert.Contains("written to /nowhere/mail instead of sent", logged, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Start_LogOnlyInDevelopment_SaysMailGoesToTheLog()
        {
            string logged = Assert.Single(await WarningsAsync(EmailOptions.None, Environments.Development));

            Assert.Contains("written to the log instead of sent", logged, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Start_LogOnlyOutsideDevelopment_SaysAForgottenPasswordCannotBeReset()
        {
            string logged = Assert.Single(await WarningsAsync(EmailOptions.None, Environments.Production));

            Assert.Contains("a forgotten password cannot be reset", logged, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Start_Postmark_SaysNothing()
        {
            Assert.Empty(await WarningsAsync(new PostmarkEmail("server-token", From), Environments.Production));
        }

        private static async Task<List<string>> WarningsAsync(EmailOptions options, string environment)
        {
            RecordingLogger<EmailSenderStartupWarning> logger = new RecordingLogger<EmailSenderStartupWarning>();
            await new EmailSenderStartupWarning(options, HostEnvironments.Named(environment), logger).StartAsync(CancellationToken.None);
            return logger.Messages;
        }
    }
}
