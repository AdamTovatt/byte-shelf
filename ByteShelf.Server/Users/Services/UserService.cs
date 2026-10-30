using System.Data.Common;
using ByteShelf.Server.Shared.Auth;
using ByteShelf.Server.Shared.Database;
using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Users.Exceptions;
using ByteShelf.Server.Users.Models;
using ByteShelf.Server.Users.Repositories;
using EasyReasy.Auth;
using EasyReasy.Database;

namespace ByteShelf.Server.Users.Services
{
    /// <summary>
    /// Implementation of <see cref="IUserService"/> that manages users across the two-table design
    /// (<c>user_account</c> and <c>user_login</c> tables).
    /// </summary>
    public class UserService : IUserService
    {
        public const int MaxEmailLength = 254;
        public const int MaxDisplayNameLength = 100;

        private readonly IUserRepository _userRepository;
        private readonly IUserLoginRepository _userLoginRepository;
        private readonly IPasswordHasher _passwordHasher;
        private readonly IDbSessionFactory _sessionFactory;
        private readonly TimeProvider _timeProvider;
        private readonly Lazy<string> _unknownEmailHash;

        public UserService(
            IUserRepository userRepository,
            IUserLoginRepository userLoginRepository,
            IPasswordHasher passwordHasher,
            IDbSessionFactory sessionFactory,
            TimeProvider timeProvider)
        {
            _userRepository = userRepository;
            _userLoginRepository = userLoginRepository;
            _passwordHasher = passwordHasher;
            _sessionFactory = sessionFactory;
            _timeProvider = timeProvider;

            // Checked against when the email is unknown, so that path costs the same hashing as a
            // wrong password and a login's timing does not tell which emails have accounts.
            _unknownEmailHash = new Lazy<string>(() => passwordHasher.HashPassword(Guid.NewGuid().ToString("N")));
        }

        /// <inheritdoc />
        public async Task<User?> ValidateCredentialsAsync(string email, string password)
        {
            UserCredentials? credentials = await _userLoginRepository.GetCredentialsByEmailAsync(email);
            if (credentials == null)
            {
                _passwordHasher.ValidatePassword(password, _unknownEmailHash.Value);
                return null;
            }

            return _passwordHasher.ValidatePassword(password, credentials.PasswordHash) ? credentials.User : null;
        }

        /// <inheritdoc />
        public async Task<User?> GetUserByIdAsync(Guid userId)
        {
            return await _userRepository.GetByIdAsync(userId);
        }

        /// <inheritdoc />
        public async Task<User> CreateUserAsync(string email, string displayName, string password, IEnumerable<string>? roles = null)
        {
            ValidateEmail(email);
            ValidateDisplayName(displayName);
            PasswordPolicy.Validate(password);

            List<string> rolesList = (roles ?? Enumerable.Empty<string>()).Distinct().ToList();
            foreach (string role in rolesList)
            {
                UserRole.Validate(role);
            }

            string passwordHash = _passwordHasher.HashPassword(password);
            DateTimeOffset now = _timeProvider.GetUtcNow();
            User user = new User
            {
                Id = Guid.NewGuid(),
                Email = email,
                DisplayName = displayName,
                Roles = rolesList,
            };

            try
            {
                await using (IDbTransactionSession session = await _sessionFactory.CreateSessionWithTransactionAsync())
                {
                    await _userRepository.InsertAsync(user, now, session);
                    await _userLoginRepository.CreateLoginAsync(user.Id, UserLoginRepository.LocalProvider, email, passwordHash, now, session);
                    await session.CommitAsync();
                }

                return user;
            }
            catch (DbException exception) when (SqliteUniqueViolation.Is(exception))
            {
                throw new DuplicateEmailException(email);
            }
        }

        /// <inheritdoc />
        public async Task<bool> UpdatePasswordAsync(Guid userId, string currentPassword, string newPassword)
        {
            PasswordPolicy.Validate(newPassword);

            User? user = await _userRepository.GetByIdAsync(userId);
            if (user == null)
            {
                return false;
            }

            UserCredentials? credentials = await _userLoginRepository.GetCredentialsByEmailAsync(user.Email);
            if (credentials == null || !_passwordHasher.ValidatePassword(currentPassword, credentials.PasswordHash))
            {
                return false;
            }

            return await _userLoginRepository.UpdateCredentialAsync(
                userId: userId,
                provider: UserLoginRepository.LocalProvider,
                newCredential: _passwordHasher.HashPassword(newPassword));
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<User>> GetAllUsersAsync()
        {
            return await _userRepository.GetAllAsync();
        }

        /// <inheritdoc />
        public async Task UpdateRolesAsync(Guid userId, IEnumerable<string> roles)
        {
            List<string> rolesList = roles.Distinct().ToList();
            foreach (string role in rolesList)
            {
                UserRole.Validate(role);
            }

            await using (IDbTransactionSession session = await _sessionFactory.CreateSessionWithTransactionAsync())
            {
                User user = await _userRepository.GetByIdAsync(userId, session)
                    ?? throw new NotFoundException($"There is no user {userId}.");

                // One write transaction at a time, so two admins demoting each other cannot both pass.
                bool removesAdmin = user.Roles.Contains(UserRole.Admin) && !rolesList.Contains(UserRole.Admin);
                if (removesAdmin && await _userRepository.CountWithRoleAsync(UserRole.Admin, session) <= 1)
                {
                    throw new ConflictException("This is the only admin; make someone else an admin first.");
                }

                await _userRepository.UpdateRolesAsync(userId, rolesList, _timeProvider.GetUtcNow(), session);
                await session.CommitAsync();
            }
        }

        /// <inheritdoc />
        public async Task<int> GetUserCountAsync()
        {
            return await _userRepository.GetCountAsync();
        }

        private static void ValidateEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email) || !email.Contains('@') || email.Length > MaxEmailLength)
            {
                throw new ArgumentException($"A valid email address of at most {MaxEmailLength} characters is required.");
            }
        }

        private static void ValidateDisplayName(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > MaxDisplayNameLength)
            {
                throw new ArgumentException($"A display name of 1 to {MaxDisplayNameLength} characters is required.");
            }
        }
    }
}
