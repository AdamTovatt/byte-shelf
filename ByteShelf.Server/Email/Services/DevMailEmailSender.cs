using ByteShelf.Server.Email.Models;

namespace ByteShelf.Server.Email.Services
{
    /// <summary>
    /// The <see cref="IEmailSender"/> on a dev server remote-claude started (<see cref="DevMailEmail"/>):
    /// nothing is sent, and each message is written into that folder as one JSON file for remote-claude to
    /// show, links included, so a developer can follow a reset link from there. A write that fails is a
    /// failed send, logged without the message's body.
    /// </summary>
    public sealed class DevMailEmailSender : IEmailSender
    {
        private readonly DevMailEmail _settings;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<DevMailEmailSender> _logger;

        public DevMailEmailSender(DevMailEmail settings, TimeProvider timeProvider, ILogger<DevMailEmailSender> logger)
        {
            _settings = settings;
            _timeProvider = timeProvider;
            _logger = logger;
        }

        public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            string path;
            try
            {
                path = await DevMailFolder.WriteAsync(_settings.Directory, message, _settings.From, _timeProvider.GetUtcNow(), cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // Everything but the caller's own cancellation, which propagates as cancellation.
                _logger.LogError(exception, "Could not write an email to {To} into the dev mail folder {Directory}.", message.To, _settings.Directory);
                return new EmailSendResult { IsSuccess = false, ErrorMessage = $"Could not write to the dev mail folder: {exception.Message}" };
            }

            _logger.LogInformation("Email to {To} ({Subject}) written to {Path}, not sent.", message.To, message.Subject, path);
            return new EmailSendResult { IsSuccess = true };
        }
    }
}
