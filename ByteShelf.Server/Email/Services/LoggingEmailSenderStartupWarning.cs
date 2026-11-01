namespace ByteShelf.Server.Email.Services
{
    /// <summary>
    /// Logs a startup warning while <see cref="IEmailSender"/> resolves to
    /// <see cref="LoggingEmailSender"/>, so a deployment without a mail provider is loudly visible. The
    /// warning disappears once a real sender is registered.
    /// </summary>
    public class LoggingEmailSenderStartupWarning : IHostedService
    {
        private readonly IEmailSender _emailSender;
        private readonly IHostEnvironment _environment;
        private readonly ILogger<LoggingEmailSenderStartupWarning> _logger;

        public LoggingEmailSenderStartupWarning(IEmailSender emailSender, IHostEnvironment environment, ILogger<LoggingEmailSenderStartupWarning> logger)
        {
            _emailSender = emailSender;
            _environment = environment;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (_emailSender is LoggingEmailSender && _environment.IsDevelopment())
            {
                _logger.LogWarning("No mail provider is configured: emails, password-reset links included, are written to the log instead of sent.");
            }
            else if (_emailSender is LoggingEmailSender)
            {
                _logger.LogWarning("No mail provider is configured: emails, password-reset links included, are not sent, so a forgotten password cannot be reset.");
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
