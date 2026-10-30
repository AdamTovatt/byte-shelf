namespace ByteShelf.Server.Email.Services
{
    /// <summary>
    /// Logs a startup warning while <see cref="IEmailSender"/> resolves to
    /// <see cref="LoggingEmailSender"/>, so a deployment that forgot to swap in a real provider
    /// is loudly visible. The warning disappears automatically once a production sender is
    /// registered.
    /// </summary>
    public class LoggingEmailSenderStartupWarning : IHostedService
    {
        private readonly IEmailSender _emailSender;
        private readonly ILogger<LoggingEmailSenderStartupWarning> _logger;

        public LoggingEmailSenderStartupWarning(IEmailSender emailSender, ILogger<LoggingEmailSenderStartupWarning> logger)
        {
            _emailSender = emailSender;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (_emailSender is LoggingEmailSender)
            {
                _logger.LogWarning(
                    "IEmailSender is LoggingEmailSender — outbound email (including password-reset links) is written to the log only. Swap in a real provider via DI for production.");
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
