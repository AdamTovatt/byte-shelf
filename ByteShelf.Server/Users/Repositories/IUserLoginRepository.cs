using ByteShelf.Server.Users.Models;
using EasyReasy.Database;

namespace ByteShelf.Server.Users.Repositories
{
    public interface IUserLoginRepository
    {
        /// <summary>The user whose local login is <paramref name="email"/> (case-insensitive), with its password hash.</summary>
        Task<UserCredentials?> GetCredentialsByEmailAsync(string email, IDbSession? session = null);

        Task CreateLoginAsync(Guid userId, string provider, string providerUserId, string? credential, DateTimeOffset createdAt, IDbSession session);

        /// <summary>Replaces the credential on a user's login row for <paramref name="provider"/>. False when there is none.</summary>
        Task<bool> UpdateCredentialAsync(Guid userId, string provider, string newCredential, IDbSession? session = null);
    }
}
