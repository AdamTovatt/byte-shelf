using ByteShelf.Server.Email.Models;

namespace ByteShelf.Server.Email.Services
{
    /// <summary>
    /// The <see cref="IEmailSender"/> while no mail provider is configured: nothing is delivered. In
    /// Development it writes the whole message to the log, links included, so a developer can follow a
    /// password-reset link. Anywhere else it logs only that an email was asked for and could not be sent,
    /// since a reset link in a log is a way into the account for whoever reads the log.
    /// <see cref="LoggingEmailSenderStartupWarning"/> says at startup that email is not delivered.
    /// </summary>
    public class LoggingEmailSender : IEmailSender
    {
        private readonly ILogger<LoggingEmailSender> _logger;
        private readonly bool _logsBody;

        public LoggingEmailSender(ILogger<LoggingEmailSender> logger, IHostEnvironment environment)
        {
            _logger = logger;
            _logsBody = environment.IsDevelopment();
        }

        public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            if (_logsBody)
            {
                _logger.LogInformation(
                    "Email (not sent, no mail provider): To={To}, Subject={Subject}, Body: {TextBody}",
                    message.To,
                    message.Subject,
                    message.TextBody);
            }
            else
            {
                _logger.LogWarning(
                    "An email ({Tag}) to {To} was asked for, but no mail provider is configured, so it was not sent.",
                    message.Tag ?? "untagged",
                    message.To);
            }

            return Task.FromResult(new EmailSendResult
            {
                IsSuccess = true,
                MessageId = null,
                ErrorMessage = null,
            });
        }
    }
}
