using ByteShelf.Server.Tests.TestUtilities;
using ByteShelf.Server.Users.Models;
using ByteShelf.Server.Users.Repositories;
using ByteShelf.Server.Users.Services;
using EasyReasy.Auth;
using EasyReasy.Database;
using EasyReasy.Database.Mapping;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ByteShelf.Server.Tests.Users
{
    public class ExpiredTokenCleanupWorkerTests : ServiceTestBase
    {
        [Fact]
        public async Task ExecuteAsync_FirstPass_DeletesExpiredTokensOfBothKindsAndKeepsLiveOnes()
        {
            User user = await Services.GetRequiredService<IUserService>().CreateUserAsync("user@example.com", "User", "password123");
            IPasswordResetTokenRepository resetTokens = Services.GetRequiredService<IPasswordResetTokenRepository>();
            await resetTokens.CreateAsync(user.Id, "expired-reset", TestInstants.Now.AddHours(-2), TestInstants.Now.AddHours(-1));
            await resetTokens.CreateAsync(user.Id, "live-reset", TestInstants.Now, TestInstants.Now.AddHours(1));
            IRefreshTokenStore refreshTokens = Services.GetRequiredService<IRefreshTokenStore>();
            await refreshTokens.StoreAsync(NewRefreshToken("expired-refresh", TestInstants.Now.UtcDateTime.AddDays(-1)));
            await refreshTokens.StoreAsync(NewRefreshToken("live-refresh", TestInstants.Now.UtcDateTime.AddDays(1)));
            ExpiredTokenCleanupWorker worker = new ExpiredTokenCleanupWorker(
                Services.GetRequiredService<IServiceScopeFactory>(), Clock, NullLogger<ExpiredTokenCleanupWorker>.Instance);

            await worker.StartAsync(CancellationToken.None);
            await WaitForCompletedRunAsync();
            await worker.StopAsync(CancellationToken.None);

            Assert.Null(await resetTokens.GetByTokenHashAsync("expired-reset"));
            Assert.NotNull(await resetTokens.GetByTokenHashAsync("live-reset"));
            Assert.Null(await refreshTokens.GetByTokenHashAsync("expired-refresh"));
            Assert.NotNull(await refreshTokens.GetByTokenHashAsync("live-refresh"));
        }

        private async Task WaitForCompletedRunAsync()
        {
            for (int attempt = 0; attempt < 200; attempt++)
            {
                await using IDbSession session = await TestRoot.SessionFactory.CreateSessionAsync();
                int completed = await session.Connection.QuerySingleAsync<int>(
                    "SELECT COUNT(*) FROM job_run WHERE completed_at IS NOT NULL");
                if (completed > 0)
                {
                    return;
                }

                await Task.Delay(25);
            }

            throw new TimeoutException("The cleanup job never completed a run.");
        }

        private static StoredRefreshToken NewRefreshToken(string tokenHash, DateTime expiresAt)
        {
            return new StoredRefreshToken
            {
                TokenHash = tokenHash,
                Subject = Guid.NewGuid().ToString("D"),
                AuthType = AuthRequestValidationService.AuthType,
                FamilyId = Guid.NewGuid().ToString("N"),
                CreatedAt = TestInstants.Now.UtcDateTime.AddDays(-2),
                ExpiresAt = expiresAt,
            };
        }
    }
}
