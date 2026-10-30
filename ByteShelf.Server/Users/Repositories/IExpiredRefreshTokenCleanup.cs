namespace ByteShelf.Server.Users.Repositories
{
    /// <summary>
    /// Deletion of expired refresh-token rows, exposed as its own interface so the cleanup job does
    /// not depend on the whole store contract.
    /// </summary>
    public interface IExpiredRefreshTokenCleanup
    {
        /// <summary>Deletes all refresh tokens that expired before <paramref name="expiredBefore"/>; answers how many.</summary>
        Task<int> DeleteExpiredTokensAsync(DateTimeOffset expiredBefore);
    }
}
