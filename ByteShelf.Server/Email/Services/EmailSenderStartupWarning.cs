namespace ByteShelf.Server.Email.Services
{
    /// <summary>
    /// Logs a warning at startup while mail is not sent (<see cref="DevMailEmail"/> or <see cref="LogOnlyEmail"/>),
    /// saying where it goes instead, so a deployment that cannot deliver a reset link is loudly visible.
    /// Nothing is logged once Postmark is configured.
    /// </summary>
    public sealed class EmailSenderStartupWarning : IHostedService
    {
        private readonly EmailOptions _options;
        private readonly IHostEnvironment _environment;
        private readonly ILogger<EmailSenderStartupWarning> _logger;

        public EmailSenderStartupWarning(EmailOptions options, IHostEnvironment environment, ILogger<EmailSenderStartupWarning> logger)
        {
            _options = options;
            _environment = environment;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            switch (_options)
            {
                case DevMailEmail devMail:
                    _logger.LogWarning("No mail provider is configured: emails, password-reset links included, are written to {Directory} instead of sent.", devMail.Directory);
                    break;
                case LogOnlyEmail when _environment.IsDevelopment():
                    _logger.LogWarning("No mail provider is configured: emails, password-reset links included, are written to the log instead of sent.");
                    break;
                case LogOnlyEmail:
                    _logger.LogWarning("No mail provider is configured: emails, password-reset links included, are not sent, so a forgotten password is reset only with the server's --reset-password.");
                    break;
                case PostmarkEmail:
                    break;
                default:
                    throw new InvalidOperationException($"No startup warning is defined for {_options.GetType().Name}.");
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
