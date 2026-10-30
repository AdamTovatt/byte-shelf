using ByteShelf.Server.Users.Models;
using EasyReasy.Database;

namespace ByteShelf.Server.Users.Repositories
{
    public interface IUserRepository
    {
        Task<User?> GetByIdAsync(Guid id, IDbSession? session = null);

        Task<IReadOnlyList<User>> GetAllAsync(IDbSession? session = null);

        Task InsertAsync(User user, DateTimeOffset createdAt, IDbSession session);

        /// <summary>Replaces a user's roles. False when there is no such user.</summary>
        Task<bool> UpdateRolesAsync(Guid userId, IReadOnlyList<string> roles, DateTimeOffset updatedAt, IDbSession session);

        Task<int> GetCountAsync(IDbSession? session = null);

        /// <summary>How many users hold <paramref name="role"/>.</summary>
        Task<int> CountWithRoleAsync(string role, IDbSession? session = null);
    }
}
