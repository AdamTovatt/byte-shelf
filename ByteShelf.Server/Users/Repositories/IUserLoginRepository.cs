using ByteShelf.Server.Users.Models;
using EasyReasy.Database;

namespace ByteShelf.Server.Users.Repositories
{
    /// <summary>The <c>user_login</c> table. Every write logs the change, credential included, in the caller's transaction.</summary>
    public interface IUserLoginRepository
    {
        /// <summary>The user whose local login is <paramref name="email"/> (case-insensitive), with its password hash.</summary>
        Task<UserCredentials?> GetCredentialsByEmailAsync(string email, IDbSession? session = null);

        Task CreateLoginAsync(Guid userId, string provider, string providerUserId, string? credential, DateTimeOffset createdAt, IDbSession session);

        /// <summary>Replaces the credential on a user's login row for <paramref name="provider"/>. False when there is none.</summary>
        Task<bool> UpdateCredentialAsync(Guid userId, string provider, string newCredential, DateTimeOffset updatedAt, IDbSession session);

        /// <summary>Removes every login of the user, for deleting them.</summary>
        Task DeleteForUserAsync(Guid userId, DateTimeOffset deletedAt, IDbSession session);
    }
}
