using ByteShelf.Server.Users.Models;
using EasyReasy.Database;

namespace ByteShelf.Server.Users.Services
{
    public interface IUserService
    {
        /// <summary>
        /// Validates an email + password pair against the local login provider. Returns the user with the
        /// credential it was checked against when valid; otherwise null. Takes as long for an unknown email
        /// as for a wrong password.
        /// </summary>
        Task<UserCredentials?> ValidateCredentialsAsync(string email, string password);

        Task<User?> GetUserByIdAsync(Guid userId);

        /// <summary>The user with <paramref name="email"/>, matched without regard to case; null when there is none.</summary>
        Task<User?> GetUserByEmailAsync(string email);

        /// <summary>Creates a user account and its local login in one transaction.</summary>
        /// <exception cref="Exceptions.DuplicateEmailException">The email is already taken.</exception>
        Task<User> CreateUserAsync(string email, string displayName, string password, IEnumerable<string>? roles = null);

        /// <summary>
        /// Changes the user's password after verifying the current one; false when it does not match. Every
        /// session signed in with the old password ends at its next refresh, except
        /// <paramref name="keepSessionFamilyId"/>, the session the change was made from, when given.
        /// </summary>
        Task<bool> UpdatePasswordAsync(Guid userId, string currentPassword, string newPassword, string? keepSessionFamilyId = null);

        Task<IReadOnlyList<User>> GetAllUsersAsync();

        /// <summary>Replaces a user's roles. Takes effect on the user's next request, whatever token they hold.</summary>
        /// <exception cref="Shared.Errors.NotFoundException">There is no such user.</exception>
        /// <exception cref="Shared.Errors.ConflictException">It would leave no admin.</exception>
        Task UpdateRolesAsync(Guid userId, IEnumerable<string> roles);

        /// <summary>
        /// Deletes a user with their logins, reset tokens, shelf access and sessions. An access token they
        /// already hold is refused from then on, since every request looks the user up.
        /// </summary>
        /// <exception cref="Shared.Errors.NotFoundException">There is no such user.</exception>
        /// <exception cref="Shared.Errors.ConflictException">The user is the only admin.</exception>
        Task DeleteUserAsync(Guid userId);

        /// <summary>
        /// Removes a user with their logins, reset tokens and shelf access in the caller's transaction,
        /// without the checks <see cref="DeleteUserAsync"/> makes; false when there is no such user. End
        /// their sessions with <see cref="EndSessionsAsync"/> once it commits.
        /// </summary>
        Task<bool> RemoveUserAsync(Guid userId, DateTimeOffset at, IDbSession session);

        /// <summary>Ends every session of the user, so no refresh token of theirs works; answers how many there were.</summary>
        Task<int> EndSessionsAsync(Guid userId);

        Task<int> GetUserCountAsync();
    }
}
