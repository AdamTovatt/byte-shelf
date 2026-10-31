using System.Data.Common;
using ByteShelf.Server.Access.Repositories;
using ByteShelf.Server.Shared.Auth;
using ByteShelf.Server.Shared.Database;
using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Users.Exceptions;
using ByteShelf.Server.Users.Models;
using ByteShelf.Server.Users.Repositories;
using ByteShelfCommon;
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
        private readonly IUserRepository _userRepository;
        private readonly IUserLoginRepository _userLoginRepository;
        private readonly IShelfGrantRepository _grantRepository;
        private readonly IRefreshTokenStore _refreshTokenStore;
        private readonly IPasswordHasher _passwordHasher;
        private readonly IDbSessionFactory _sessionFactory;
        private readonly TimeProvider _timeProvider;
        private readonly Lazy<string> _unknownEmailHash;

        public UserService(
            IUserRepository userRepository,
            IUserLoginRepository userLoginRepository,
            IShelfGrantRepository grantRepository,
            IRefreshTokenStore refreshTokenStore,
            IPasswordHasher passwordHasher,
            IDbSessionFactory sessionFactory,
            TimeProvider timeProvider)
        {
            _userRepository = userRepository;
            _userLoginRepository = userLoginRepository;
            _grantRepository = grantRepository;
            _refreshTokenStore = refreshTokenStore;
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
            UserFields.ValidateEmail(email);
            displayName = UserFields.NormalizeDisplayName(displayName);
            PasswordPolicy.Validate(password);

            IReadOnlyList<string> rolesList = UserRoles.Normalize(roles);

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

            string passwordHash = _passwordHasher.HashPassword(newPassword);

            await using (IDbTransactionSession session = await _sessionFactory.CreateSessionWithTransactionAsync())
            {
                if (!await _userLoginRepository.UpdateCredentialAsync(userId, UserLoginRepository.LocalProvider, passwordHash, _timeProvider.GetUtcNow(), session))
                {
                    return false;
                }

                await session.CommitAsync();
                return true;
            }
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<User>> GetAllUsersAsync()
        {
            return await _userRepository.GetAllAsync();
        }

        /// <inheritdoc />
        public async Task UpdateRolesAsync(Guid userId, IEnumerable<string> roles)
        {
            IReadOnlyList<string> rolesList = UserRoles.Normalize(roles);

            await using (IDbTransactionSession session = await _sessionFactory.CreateSessionWithTransactionAsync())
            {
                User user = await _userRepository.GetByIdAsync(userId, session)
                    ?? throw new NotFoundException($"There is no user {userId}.");

                // One write transaction at a time, so two admins demoting each other cannot both pass.
                bool removesAdmin = user.Roles.Contains(UserRoles.Admin) && !rolesList.Contains(UserRoles.Admin);
                if (removesAdmin && await _userRepository.CountWithRoleAsync(UserRoles.Admin, session) <= 1)
                {
                    throw new ConflictException("This is the only admin; make someone else an admin first.");
                }

                await _userRepository.UpdateRolesAsync(userId, rolesList, _timeProvider.GetUtcNow(), session);
                await session.CommitAsync();
            }
        }

        /// <inheritdoc />
        public async Task DeleteUserAsync(Guid userId)
        {
            await using (IDbTransactionSession session = await _sessionFactory.CreateSessionWithTransactionAsync())
            {
                User user = await _userRepository.GetByIdAsync(userId, session)
                    ?? throw new NotFoundException($"There is no user {userId}.");

                if (user.Roles.Contains(UserRoles.Admin) && await _userRepository.CountWithRoleAsync(UserRoles.Admin, session) <= 1)
                {
                    throw new ConflictException("This is the only admin; make someone else an admin first.");
                }

                DateTimeOffset now = _timeProvider.GetUtcNow();
                await _grantRepository.DeleteForUserAsync(userId, now, session);
                await _userLoginRepository.DeleteForUserAsync(userId, now, session);
                await _userRepository.DeleteAsync(userId, now, session);
                await session.CommitAsync();
            }

            // After the commit, as a password reset does. A refresh would be refused anyway, since the
            // user is gone; this also drops the stored tokens.
            await _refreshTokenStore.InvalidateAllFamiliesForUserAsync(userId.ToString("D"));
        }

        /// <inheritdoc />
        public async Task<int> GetUserCountAsync()
        {
            return await _userRepository.GetCountAsync();
        }
    }
}
