using ByteShelf.Server.Users.Models;
using EasyReasy.Database;

namespace ByteShelf.Server.Users.Repositories
{
    public interface IPasswordResetTokenRepository
    {
        Task CreateAsync(Guid userId, string tokenHash, DateTimeOffset createdAt, DateTimeOffset expiresAt, IDbSession? session = null);

        Task<PasswordResetTokenRecord?> GetByTokenHashAsync(string tokenHash, IDbSession? session = null);

        /// <summary>Marks a token used, only if it was not already. False otherwise.</summary>
        Task<bool> MarkAsUsedAsync(Guid tokenId, DateTimeOffset usedAt, IDbSession? session = null);

        /// <summary>Marks every unused token of the user used, so no other link still works.</summary>
        Task InvalidateAllForUserAsync(Guid userId, DateTimeOffset invalidatedAt, IDbSession? session = null);

        Task<int> DeleteExpiredAsync(DateTimeOffset expiredBefore, IDbSession? session = null);
    }
}
