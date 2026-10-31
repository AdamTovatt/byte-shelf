using ByteShelf.Server.Shared.Auth;
using ByteShelf.Server.Users.Models;
using ByteShelf.Server.Users.Repositories;
using ByteShelfCommon;
using EasyReasy.Auth;
using EasyReasy.Database;

namespace ByteShelf.Server.Users.Services
{
    /// <summary>
    /// Implementation of <see cref="IPasswordResetService"/> that orchestrates token generation,
    /// storage, validation, and password update for password resets. All time-bounding runs
    /// through the injected <see cref="TimeProvider"/>.
    /// </summary>
    public class PasswordResetService : IPasswordResetService
    {
        /// <summary>
        /// How long a reset link stays valid. Internal so the email notifier can state the same
        /// window in its copy instead of hardcoding it.
        /// </summary>
        internal static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(1);

        private readonly IPasswordResetTokenRepository _tokenRepository;
        private readonly IUserLoginRepository _userLoginRepository;
        private readonly IPasswordResetTokenHandler _tokenHandler;
        private readonly IPasswordHasher _passwordHasher;
        private readonly ResetLinkOutbox _outbox;
        private readonly IRefreshTokenStore _refreshTokenStore;
        private readonly IDbSessionFactory _sessionFactory;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<PasswordResetService> _logger;

        public PasswordResetService(
            IPasswordResetTokenRepository tokenRepository,
            IUserLoginRepository userLoginRepository,
            IPasswordResetTokenHandler tokenHandler,
            IPasswordHasher passwordHasher,
            ResetLinkOutbox outbox,
            IRefreshTokenStore refreshTokenStore,
            IDbSessionFactory sessionFactory,
            TimeProvider timeProvider,
            ILogger<PasswordResetService> logger)
        {
            _tokenRepository = tokenRepository;
            _userLoginRepository = userLoginRepository;
            _tokenHandler = tokenHandler;
            _passwordHasher = passwordHasher;
            _outbox = outbox;
            _refreshTokenStore = refreshTokenStore;
            _sessionFactory = sessionFactory;
            _timeProvider = timeProvider;
            _logger = logger;
        }

        /// <inheritdoc />
        public async Task RequestResetAsync(string email)
        {
            UserCredentials? credentials = await _userLoginRepository.GetCredentialsByEmailAsync(email);
            if (credentials == null)
            {
                return;
            }

            // Earlier links stay valid until they expire: invalidating them here would let anyone who
            // knows the address keep breaking the link the owner is about to use. Completing any one
            // reset invalidates all of them.
            DateTimeOffset now = _timeProvider.GetUtcNow();
            PasswordResetToken token = _tokenHandler.GenerateResetToken();
            await _tokenRepository.CreateAsync(credentials.User.Id, token.TokenHash, now, now + TokenLifetime);

            // Sent in the background, so a known address answers as fast as an unknown one, give or
            // take the one insert above.
            if (!_outbox.TryEnqueue(new ResetLink(credentials.User.Email, token.Token)))
            {
                _logger.LogWarning("The reset link outbox is full; dropped the link for {Email}.", credentials.User.Email);
            }
        }

        /// <inheritdoc />
        public async Task<bool> CompleteResetAsync(string token, string newPassword)
        {
            PasswordPolicy.Validate(newPassword);

            // Must be the hash SecurePasswordResetTokenHandler stores (lowercase-hex SHA-256 of the UTF-8
            // token): the library does not expose its hashing, and the hash is the only lookup key. Drift
            // fails closed: the lookup misses and every reset breaks loudly; ValidateResetToken still
            // guards acceptance after the lookup.
            PasswordResetTokenRecord? record = await _tokenRepository.GetByTokenHashAsync(SecretHash.Sha256Hex(token));
            if (record == null || !_tokenHandler.ValidateResetToken(token, record.TokenHash))
            {
                return false;
            }

            DateTimeOffset now = _timeProvider.GetUtcNow();
            if (record.UsedAt != null || now > record.ExpiresAt)
            {
                return false;
            }

            string passwordHash = _passwordHasher.HashPassword(newPassword);

            await using (IDbTransactionSession session = await _sessionFactory.CreateSessionWithTransactionAsync())
            {
                if (!await _tokenRepository.MarkAsUsedAsync(record.Id, now, session))
                {
                    return false;
                }

                if (!await _userLoginRepository.UpdateCredentialAsync(record.UserId, UserLoginRepository.LocalProvider, passwordHash, now, session))
                {
                    return false;
                }

                await _tokenRepository.InvalidateAllForUserAsync(record.UserId, now, session);
                await session.CommitAsync();
            }

            // A reset means the old password may have been compromised — revoke every live
            // session so only the holder of the new password stays logged in. Runs after the
            // commit: the password change must not be rolled back if revocation fails.
            int revokedFamilies = await _refreshTokenStore.InvalidateAllFamiliesForUserAsync(record.UserId.ToString("D"));
            if (revokedFamilies > 0)
            {
                _logger.LogInformation("Password reset revoked {Count} active session(s) for user {UserId}.", revokedFamilies, record.UserId);
            }

            return true;
        }
    }
}
