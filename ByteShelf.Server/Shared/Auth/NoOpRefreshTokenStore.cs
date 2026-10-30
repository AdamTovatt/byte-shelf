using EasyReasy.Auth;

namespace ByteShelf.Server.Shared.Auth
{
    /// <summary>
    /// A no-op implementation of <see cref="IRefreshTokenStore"/> used exclusively
    /// in schema-only mode. No tokens are stored or validated — the refresh endpoint
    /// exists only to appear in the OpenAPI spec.
    /// </summary>
    public class NoOpRefreshTokenStore : IRefreshTokenStore
    {
        public Task StoreAsync(StoredRefreshToken refreshToken, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<StoredRefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<StoredRefreshToken?>(null);
        }

        public Task<bool> MarkAsConsumedAsync(string tokenHash, DateTime consumedAt, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }

        public Task InvalidateFamilyAsync(string familyId, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<int> InvalidateAllFamiliesForUserAsync(string subject, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0);
        }
    }
}
