using ByteShelf.Server.Users.Models;
using EasyReasy.Database;

namespace ByteShelf.Server.Users.Repositories
{
    /// <summary>The <c>user_login</c> table. Every write logs the change, credential included, in the caller's transaction.</summary>
    public interface IUserLoginRepository
    {
        /// <summary>The user whose local login is <paramref name="email"/> (case-insensitive), with its password hash.</summary>
        Task<UserCredentials?> GetCredentialsByEmailAsync(string email, IDbSession? session = null);

        /// <summary>The user <paramref name="userId"/> with their local login's password hash; null without either.</summary>
        Task<UserCredentials?> GetCredentialsByUserIdAsync(Guid userId, IDbSession? session = null);

        Task CreateLoginAsync(Guid userId, string provider, string providerUserId, string? credential, DateTimeOffset createdAt, IDbSession session);

        /// <summary>Replaces the credential on a user's login row for <paramref name="provider"/>. False when there is none.</summary>
        Task<bool> UpdateCredentialAsync(Guid userId, string provider, string newCredential, DateTimeOffset updatedAt, IDbSession session);

        /// <summary>Removes every login of the user, for deleting them.</summary>
        Task DeleteForUserAsync(Guid userId, DateTimeOffset deletedAt, IDbSession session);

        /// <summary>
        /// Writes a login as a primary's change log holds it, logged as <paramref name="action"/>. A
        /// different login taking the same provider identity, or the same user's provider, is removed
        /// first: the log brings it back if it still exists.
        /// </summary>
        Task UpsertReplicatedAsync(UserLogin login, string action, DateTimeOffset at, IDbSession session);

        /// <summary>Removes a login by id; false when there is none.</summary>
        Task<bool> DeleteByIdAsync(Guid id, DateTimeOffset deletedAt, IDbSession session);
    }
}
