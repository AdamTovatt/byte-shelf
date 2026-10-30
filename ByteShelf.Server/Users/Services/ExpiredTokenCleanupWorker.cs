using ByteShelf.Server.Jobs.Models;
using ByteShelf.Server.Jobs.Services;
using ByteShelf.Server.Users.Models;
using ByteShelf.Server.Users.Repositories;

namespace ByteShelf.Server.Users.Services
{
    /// <summary>Deletes expired refresh and password-reset tokens, at startup and then daily, so neither table grows without bound.</summary>
    public class ExpiredTokenCleanupWorker : PeriodicJobWorker<ExpiredTokenCleanupSummary>
    {
        private readonly TimeProvider _timeProvider;

        public ExpiredTokenCleanupWorker(IServiceScopeFactory scopeFactory, TimeProvider timeProvider, ILogger<ExpiredTokenCleanupWorker> logger)
            : base(scopeFactory, timeProvider, logger)
        {
            _timeProvider = timeProvider;
        }

        protected override string JobName => JobNames.ExpiredTokenCleanup;

        protected override TimeSpan Interval => TimeSpan.FromDays(1);

        protected override async Task<ExpiredTokenCleanupSummary> RunOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();

            int refreshTokens = await services.GetRequiredService<IExpiredRefreshTokenCleanup>().DeleteExpiredTokensAsync(now);
            int resetTokens = await services.GetRequiredService<IPasswordResetTokenRepository>().DeleteExpiredAsync(now);

            return new ExpiredTokenCleanupSummary(refreshTokens, resetTokens);
        }
    }
}
