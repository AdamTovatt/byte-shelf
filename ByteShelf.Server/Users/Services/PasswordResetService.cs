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
        private readonly IUserService _userService;
        private readonly IDbSessionFactory _sessionFactory;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<PasswordResetService> _logger;

        public PasswordResetService(
            IPasswordResetTokenRepository tokenRepository,
            IUserLoginRepository userLoginRepository,
            IPasswordResetTokenHandler tokenHandler,
            IPasswordHasher passwordHasher,
            ResetLinkOutbox outbox,
            IUserService userService,
            IDbSessionFactory sessionFactory,
            TimeProvider timeProvider,
            ILogger<PasswordResetService> logger)
        {
            _tokenRepository = tokenRepository;
            _userLoginRepository = userLoginRepository;
            _tokenHandler = tokenHandler;
            _passwordHasher = passwordHasher;
            _outbox = outbox;
            _userService = userService;
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
                if (!await _tokenRepository.MarkAsUsedAsync(record.Id, now, session)
                    || !await ReplaceCredentialAsync(record.UserId, passwordHash, now, session))
                {
                    return false;
                }

                await session.CommitAsync();
            }

            await EndSessionsAsync(record.UserId);
            return true;
        }

        /// <inheritdoc />
        public async Task<bool> SetPasswordAsync(Guid userId, string newPassword)
        {
            PasswordPolicy.Validate(newPassword);
            string passwordHash = _passwordHasher.HashPassword(newPassword);

            await using (IDbTransactionSession session = await _sessionFactory.CreateSessionWithTransactionAsync())
            {
                if (!await ReplaceCredentialAsync(userId, passwordHash, _timeProvider.GetUtcNow(), session))
                {
                    return false;
                }

                await session.CommitAsync();
            }

            await EndSessionsAsync(userId);
            return true;
        }

        /// <summary>
        /// Gives the user's login <paramref name="passwordHash"/>, and ends every reset link of theirs; false
        /// when there is no such login. A password set without the current one, by a reset link or by
        /// whoever runs the server, is followed by <see cref="EndSessionsAsync"/> once it commits.
        /// </summary>
        private async Task<bool> ReplaceCredentialAsync(Guid userId, string passwordHash, DateTimeOffset now, IDbSession session)
        {
            if (!await _userLoginRepository.UpdateCredentialAsync(userId, UserLoginRepository.LocalProvider, passwordHash, now, session))
            {
                return false;
            }

            await _tokenRepository.InvalidateAllForUserAsync(userId, now, session);
            return true;
        }

        /// <summary>
        /// A password set without the current one means the old one may have been compromised: every
        /// session ends, so only the holder of the new password stays signed in. The new credential alone
        /// already refuses their refreshes (CredentialStamp); this also drops the stored tokens. Runs after
        /// the commit, and a failure here is logged rather than thrown: the password has changed, which the
        /// caller must be told, and the sessions it would drop can no longer refresh anyway.
        /// </summary>
        private async Task EndSessionsAsync(Guid userId)
        {
            try
            {
                int revokedFamilies = await _userService.EndSessionsAsync(userId);
                if (revokedFamilies > 0)
                {
                    _logger.LogInformation("Setting a new password revoked {Count} active session(s) for user {UserId}.", revokedFamilies, userId);
                }
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "The password of user {UserId} changed, but dropping their sessions failed; they cannot refresh with the old password.", userId);
            }
        }
    }
}
