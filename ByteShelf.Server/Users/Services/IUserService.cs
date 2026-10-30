using ByteShelf.Server.Users.Models;

namespace ByteShelf.Server.Users.Services
{
    public interface IUserService
    {
        /// <summary>
        /// Validates an email + password pair against the local login provider. Returns the user when
        /// valid; otherwise null. Takes as long for an unknown email as for a wrong password.
        /// </summary>
        Task<User?> ValidateCredentialsAsync(string email, string password);

        Task<User?> GetUserByIdAsync(Guid userId);

        /// <summary>Creates a user account and its local login in one transaction.</summary>
        /// <exception cref="Exceptions.DuplicateEmailException">The email is already taken.</exception>
        Task<User> CreateUserAsync(string email, string displayName, string password, IEnumerable<string>? roles = null);

        /// <summary>Changes the user's password after verifying the current one; false when it does not match.</summary>
        Task<bool> UpdatePasswordAsync(Guid userId, string currentPassword, string newPassword);

        Task<IReadOnlyList<User>> GetAllUsersAsync();

        /// <summary>Replaces a user's roles. Takes effect on the user's next token refresh.</summary>
        /// <exception cref="Shared.Errors.NotFoundException">There is no such user.</exception>
        /// <exception cref="Shared.Errors.ConflictException">It would leave no admin.</exception>
        Task UpdateRolesAsync(Guid userId, IEnumerable<string> roles);

        Task<int> GetUserCountAsync();
    }
}
