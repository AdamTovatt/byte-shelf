using System.Runtime.Versioning;
using System.Text.Json;
using ByteShelf.Server.Email;
using ByteShelf.Server.Email.Models;
using ByteShelf.Server.Email.Services;
using ByteShelf.Server.Tests.TestUtilities;
using Microsoft.Extensions.Time.Testing;

namespace ByteShelf.Server.Tests.Email
{
    /// <summary>What a dev server remote-claude started does with mail: one file per message, in remote-claude's contract.</summary>
    public sealed class DevMailEmailSenderTests : IDisposable
    {
        private const string Token = "reset-token-from-the-link";

        private static readonly DateTimeOffset SentAt = new DateTimeOffset(2026, 11, 2, 9, 30, 15, TimeSpan.Zero);

        private static readonly MailSender From = new MailSender("shelf@example.com", "Shelf");

        private static readonly EmailMessage ResetEmail = new EmailMessage
        {
            To = "user@example.com",
            Subject = "Reset your password",
            HtmlBody = $"<a href=\"https://shelf.test/reset-password?token={Token}\">Reset</a>",
            TextBody = $"Visit https://shelf.test/reset-password?token={Token}",
            Tag = "password-reset",
        };

        private readonly string _folder = Path.Combine(AppContext.BaseDirectory, "test-mail", Guid.NewGuid().ToString("N"));
        private readonly RecordingLogger<DevMailEmailSender> _log = new RecordingLogger<DevMailEmailSender>();

        public void Dispose()
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, recursive: true);
            }
        }

        [Fact]
        public async Task SendAsync_WritesOneFileInRemoteClaudesContract()
        {
            EmailSendResult result = await Sender().SendAsync(ResetEmail);

            Assert.True(result.IsSuccess);
            string path = Assert.Single(Directory.GetFiles(_folder));
            Assert.EndsWith(".json", path, StringComparison.Ordinal);
            using JsonDocument file = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            JsonElement root = file.RootElement;
            Assert.Equal(1, root.GetProperty("version").GetInt32());
            Assert.Equal("2026-11-02T09:30:15.0000000Z", root.GetProperty("sentAt").GetString());
            Assert.Equal("user@example.com", root.GetProperty("to").GetString());
            Assert.Equal("shelf@example.com", root.GetProperty("fromEmail").GetString());
            Assert.Equal("Shelf", root.GetProperty("fromName").GetString());
            Assert.Equal("Reset your password", root.GetProperty("subject").GetString());
            Assert.Equal(ResetEmail.HtmlBody, root.GetProperty("html").GetString());
            Assert.Equal(ResetEmail.TextBody, root.GetProperty("text").GetString());
            Assert.Equal("password-reset", root.GetProperty("tag").GetString());
        }

        [Fact]
        public async Task SendAsync_WritesHtmlAsItself_SoTheLinkCanBeFoundInTheFile()
        {
            await Sender().SendAsync(ResetEmail);

            // HTML-sensitive characters as themselves, not < escapes; JSON still escapes the quote.
            string written = await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(_folder)));
            Assert.Contains("<a href=\\\"https://shelf.test/reset-password?token=", written, StringComparison.Ordinal);
        }

        [UnixFact]
        [UnsupportedOSPlatform("windows")]
        public async Task SendAsync_CreatesTheFolderAndFileOwnerOnly_AndLeavesNoTemporaryFile()
        {
            await Sender().SendAsync(ResetEmail);

            string path = Assert.Single(Directory.GetFiles(_folder));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(_folder));
        }

        [Fact]
        public async Task SendAsync_WhenTheFolderCannotBeWritten_FailsAndLogsWithoutTheLink()
        {
            // A file where the folder should be: creating the folder fails.
            Directory.CreateDirectory(Path.GetDirectoryName(_folder)!);
            await File.WriteAllTextAsync(_folder, "not a folder");
            try
            {
                EmailSendResult result = await Sender().SendAsync(ResetEmail);

                Assert.False(result.IsSuccess);
                Assert.StartsWith("Could not write to the dev mail folder", result.ErrorMessage, StringComparison.Ordinal);
                string logged = Assert.Single(_log.Messages);
                Assert.Contains(_folder, logged, StringComparison.Ordinal);
                Assert.DoesNotContain(Token, logged, StringComparison.Ordinal);
            }
            finally
            {
                File.Delete(_folder);
            }
        }

        private DevMailEmailSender Sender()
        {
            return new DevMailEmailSender(new DevMailEmail(_folder, From), new FakeTimeProvider(SentAt), _log);
        }
    }
}
