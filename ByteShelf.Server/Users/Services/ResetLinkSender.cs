namespace ByteShelf.Server.Users.Services
{
    /// <summary>Sends the links queued in <see cref="ResetLinkOutbox"/>, one at a time, logging any that fail.</summary>
    public class ResetLinkSender : BackgroundService
    {
        private readonly ResetLinkOutbox _outbox;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ResetLinkSender> _logger;

        public ResetLinkSender(ResetLinkOutbox outbox, IServiceScopeFactory scopeFactory, ILogger<ResetLinkSender> logger)
        {
            _outbox = outbox;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await foreach (ResetLink link in _outbox.ReadAllAsync(stoppingToken))
                {
                    try
                    {
                        using IServiceScope scope = _scopeFactory.CreateScope();
                        IPasswordResetNotifier notifier = scope.ServiceProvider.GetRequiredService<IPasswordResetNotifier>();
                        await notifier.SendResetLinkAsync(link.Email, link.Token);
                    }
                    catch (Exception exception)
                    {
                        _logger.LogError(exception, "Failed to send a password reset link to {Email}.", link.Email);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
        }
    }
}
