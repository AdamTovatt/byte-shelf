using System.Data.Common;
using System.Text.Json;
using ByteShelf.Server.Replication;
using ByteShelf.Server.Replication.Services;
using ByteShelf.Server.Shared.Database;
using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Users.Models;
using ByteShelf.Server.Users.Repositories;
using ByteShelfCommon;
using EasyReasy.Auth;
using EasyReasy.Database;

namespace ByteShelf.Server.Users.Services
{
    /// <summary>
    /// Passkeys: adding one to the signed-in user, signing in with one alone (usernameless, user
    /// verification always required), and listing, renaming and removing them. Every passkey sits beside
    /// the user's password, which a sign-in with it still depends on: a password change or reset ends
    /// passkey sessions too (<see cref="CredentialStamp"/>).
    /// </summary>
    public class PasskeyService
    {
        /// <summary>The most characters a passkey's name can have, once trimmed.</summary>
        public const int MaxNameLength = 100;

        private readonly PasskeyRelyingParty _relyingParty;
        private readonly PasskeyChallenges _challenges;
        private readonly IPasskeyRepository _passkeyRepository;
        private readonly IUserRepository _userRepository;
        private readonly IUserLoginRepository _userLoginRepository;
        private readonly IUserService _userService;
        private readonly ReplicationState _replicationState;
        private readonly SessionIssuer _sessionIssuer;
        private readonly IDbSessionFactory _sessionFactory;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<PasskeyService> _logger;

        public PasskeyService(
            PasskeyRelyingParty relyingParty,
            PasskeyChallenges challenges,
            IPasskeyRepository passkeyRepository,
            IUserRepository userRepository,
            IUserLoginRepository userLoginRepository,
            IUserService userService,
            ReplicationState replicationState,
            SessionIssuer sessionIssuer,
            IDbSessionFactory sessionFactory,
            TimeProvider timeProvider,
            ILogger<PasskeyService> logger)
        {
            _relyingParty = relyingParty;
            _challenges = challenges;
            _passkeyRepository = passkeyRepository;
            _userRepository = userRepository;
            _userLoginRepository = userLoginRepository;
            _userService = userService;
            _replicationState = replicationState;
            _sessionIssuer = sessionIssuer;
            _sessionFactory = sessionFactory;
            _timeProvider = timeProvider;
            _logger = logger;
        }

        private static int TimeoutMilliseconds => (int)PasskeyChallenges.Lifetime.TotalMilliseconds;

        /// <summary>The user's passkeys, whether passkeys are on here, and whether one can be added here (not on a replica).</summary>
        public async Task<PasskeyList> ListAsync(Guid userId)
        {
            IReadOnlyList<StoredPasskey> passkeys = await _passkeyRepository.GetForUserAsync(userId);
            bool available = _relyingParty.RelyingParty != null;
            return new PasskeyList
            {
                Available = available,
                CanAdd = available && await _replicationState.AllowsWritesAsync(),
                Passkeys = passkeys.Select(stored => ToInfo(stored.Passkey, stored.LastUsedAt)).ToList(),
            };
        }

        /// <summary>
        /// Begins adding a passkey to <paramref name="userId"/>, once they have given their password: a
        /// passkey outlives a password reset, so a session alone, which a stolen token is, must not be
        /// enough to add one. The browser may not make one they already have. Null for a wrong password.
        /// </summary>
        /// <exception cref="NotFoundException">Passkeys are off, or the user is gone.</exception>
        public async Task<PasskeyCeremony?> BeginAddingAsync(Guid userId, string currentPassword)
        {
            WebAuthnRelyingParty relyingParty = RequireRelyingParty();
            User user = await _userRepository.GetByIdAsync(userId)
                ?? throw new NotFoundException("The signed-in user no longer exists.");

            if (!await _userService.IsPasswordAsync(userId, currentPassword))
            {
                return null;
            }

            IEnumerable<WebAuthnCredentialReference> existing = (await _passkeyRepository.GetForUserAsync(userId))
                .Select(stored => new WebAuthnCredentialReference(stored.Passkey.CredentialId, stored.Passkey.Transports));

            PublicKeyCredentialCreationOptions options = new WebAuthnOptionsGenerator(relyingParty).CreateDiscoverableRegistrationOptions(
                new PublicKeyCredentialUserEntity(UserHandleOf(userId), user.Email, user.DisplayName),
                existing,
                timeoutMilliseconds: TimeoutMilliseconds);

            return Ceremony(_challenges.Keep(options.Challenge, userId), options.ToJson());
        }

        /// <summary>Adds the passkey the browser made in the user's ceremony, under <see cref="AddPasskeyRequest.Name"/>.</summary>
        /// <exception cref="ArgumentException">The name breaks the rule, the ceremony is not the user's or ran out, or the passkey does not verify.</exception>
        /// <exception cref="ConflictException">That passkey is already added.</exception>
        public async Task<PasskeyInfo> AddAsync(Guid userId, AddPasskeyRequest request)
        {
            WebAuthnRelyingParty relyingParty = RequireRelyingParty();
            string name = DisplayText.Validate(request.Name, MaxNameLength, "A passkey's name");

            string challenge = _challenges.Take(request.CeremonyId, userId)
                ?? throw new ArgumentException("That passkey prompt is no longer open; add the passkey again.");

            WebAuthnRegistrationResult result = new WebAuthnVerifier(relyingParty).VerifyDiscoverableRegistration(
                WebAuthnRegistrationResponse.FromJson(request.Credential.GetRawText()), challenge);
            if (!result.Success || result.Credential is not WebAuthnRegisteredCredential credential)
            {
                _logger.LogInformation("A passkey for user {UserId} was refused: {Reason} {Message}", userId, result.FailureReason, result.FailureMessage);
                throw new ArgumentException("The passkey could not be verified.");
            }

            Passkey passkey = new Passkey
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                CredentialId = credential.CredentialId,
                PublicKey = credential.PublicKey,
                Transports = credential.Transports,
                Name = name,
                CreatedAt = _timeProvider.GetUtcNow(),
            };

            try
            {
                await using (IDbTransactionSession session = await _sessionFactory.CreateSessionWithTransactionAsync())
                {
                    await _passkeyRepository.InsertAsync(passkey, session);
                    await session.CommitAsync();
                }
            }
            catch (DbException exception) when (SqliteUniqueViolation.Is(exception))
            {
                throw new ConflictException("That passkey is already added.");
            }

            _logger.LogInformation("User {UserId} added passkey {PasskeyId}.", userId, passkey.Id);
            return ToInfo(passkey, lastUsedAt: null);
        }

        /// <summary>Begins signing in with a passkey, for anyone: the browser offers every passkey it holds for this server.</summary>
        /// <exception cref="NotFoundException">Passkeys are off.</exception>
        public PasskeyCeremony BeginSignIn()
        {
            PublicKeyCredentialRequestOptions options = new WebAuthnOptionsGenerator(RequireRelyingParty())
                .CreateDiscoverableAuthenticationOptions(TimeoutMilliseconds);

            return Ceremony(_challenges.Keep(options.Challenge, userId: null), options.ToJson());
        }

        /// <summary>
        /// The tokens of a session for the owner of the passkey that signed the ceremony's challenge; null
        /// when the ceremony is unknown or spent, the passkey is not one here, or it does not verify.
        /// </summary>
        /// <exception cref="NotFoundException">Passkeys are off.</exception>
        /// <exception cref="ArgumentException">The credential is not a WebAuthn assertion.</exception>
        public async Task<AuthResponse?> SignInAsync(PasskeySignInRequest request, IJwtTokenService jwtTokenService)
        {
            WebAuthnRelyingParty relyingParty = RequireRelyingParty();

            // Spent before anything is checked, so a challenge works once whatever comes of it.
            string? challenge = _challenges.Take(request.CeremonyId, userId: null);
            if (challenge == null)
            {
                return null;
            }

            WebAuthnAuthenticationResponse response = WebAuthnAuthenticationResponse.FromJson(request.Credential.GetRawText());

            // Across every user: the passkey names the user, never the request.
            StoredPasskey? stored = await _passkeyRepository.GetByCredentialIdAsync(response.Id);
            UserCredentials? credentials = stored == null ? null : await _userLoginRepository.GetCredentialsByUserIdAsync(stored.Passkey.UserId);
            if (stored == null || credentials == null)
            {
                return null;
            }

            WebAuthnAuthenticationResult assertion = new WebAuthnVerifier(relyingParty).VerifyDiscoverableAuthentication(
                response,
                challenge,
                new WebAuthnStoredCredential(stored.Passkey.CredentialId, stored.Passkey.PublicKey, stored.SignCount),
                UserHandleOf(stored.Passkey.UserId));
            if (!assertion.Success)
            {
                _logger.LogInformation("A sign-in with passkey {PasskeyId} was refused: {Reason} {Message}", stored.Passkey.Id, assertion.FailureReason, assertion.FailureMessage);
                return null;
            }

            // Written back on every success, so a counter that starts counting is compared from then on.
            await _passkeyRepository.RecordSignInAsync(stored.Passkey.Id, assertion.SignCount, _timeProvider.GetUtcNow());
            return await _sessionIssuer.IssueAsync(credentials, stored.Passkey.Id, jwtTokenService);
        }

        /// <exception cref="ArgumentException">The name breaks the rule.</exception>
        /// <exception cref="NotFoundException">The user has no such passkey.</exception>
        public async Task RenameAsync(Guid userId, Guid passkeyId, string name)
        {
            string normalized = DisplayText.Validate(name, MaxNameLength, "A passkey's name");

            await using (IDbTransactionSession session = await _sessionFactory.CreateSessionWithTransactionAsync())
            {
                if (!await _passkeyRepository.RenameAsync(userId, passkeyId, normalized, _timeProvider.GetUtcNow(), session))
                {
                    throw new NotFoundException("There is no such passkey.");
                }

                await session.CommitAsync();
            }
        }

        /// <summary>Removes the user's passkey, which ends the sessions signed in with it.</summary>
        /// <exception cref="NotFoundException">The user has no such passkey.</exception>
        public async Task RemoveAsync(Guid userId, Guid passkeyId)
        {
            await using (IDbTransactionSession session = await _sessionFactory.CreateSessionWithTransactionAsync())
            {
                if (!await _passkeyRepository.DeleteAsync(userId, passkeyId, _timeProvider.GetUtcNow(), session))
                {
                    throw new NotFoundException("There is no such passkey.");
                }

                await session.CommitAsync();
            }

            _logger.LogInformation("User {UserId} removed passkey {PasskeyId}.", userId, passkeyId);
        }

        /// <summary>
        /// The user handle a user's passkeys are made with and must present: the bytes of their id, opaque
        /// and stable, never the email the authenticator also shows.
        /// </summary>
        private static byte[] UserHandleOf(Guid userId)
        {
            return userId.ToByteArray();
        }

        private WebAuthnRelyingParty RequireRelyingParty()
        {
            return _relyingParty.RelyingParty
                ?? throw new NotFoundException("Passkeys are off on this server: it has no APP_BASE_URL a browser makes passkeys for.");
        }

        /// <remarks>
        /// The options go to text and back to a JSON element, and the browser's answers from a JSON element
        /// to text (<c>GetRawText</c>, then <c>FromJson</c>), because EasyReasy.Auth's WebAuthn types offer
        /// only text. System.Text.Json converters on those types would replace both round trips.
        /// </remarks>
        private static PasskeyCeremony Ceremony(string ceremonyId, string optionsJson)
        {
            using JsonDocument options = JsonDocument.Parse(optionsJson);
            return new PasskeyCeremony { CeremonyId = ceremonyId, Options = options.RootElement.Clone() };
        }

        private static PasskeyInfo ToInfo(Passkey passkey, DateTimeOffset? lastUsedAt)
        {
            return new PasskeyInfo
            {
                Id = passkey.Id,
                Name = passkey.Name,
                CreatedAt = passkey.CreatedAt,
                LastUsedAt = lastUsedAt,
            };
        }
    }
}
