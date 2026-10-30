using ByteShelf.Server.Email.Models;

namespace ByteShelf.Server.Email.Services
{
    /// <summary>
    /// Development default for <see cref="IEmailSender"/>: writes the message (including any
    /// links in the text body, e.g. the password-reset link) to the log instead of sending it.
    /// <see cref="LoggingEmailSenderStartupWarning"/> flags this registration as non-production
    /// at boot.
    /// </summary>
    public class LoggingEmailSender : IEmailSender
    {
        private readonly ILogger<LoggingEmailSender> _logger;

        public LoggingEmailSender(ILogger<LoggingEmailSender> logger)
        {
            _logger = logger;
        }

        public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation(
                "Email (not sent — LoggingEmailSender): To={To}, Subject={Subject}, Body: {TextBody}",
                message.To,
                message.Subject,
                message.TextBody);

            return Task.FromResult(new EmailSendResult
            {
                IsSuccess = true,
                MessageId = null,
                ErrorMessage = null,
            });
        }
    }
}
