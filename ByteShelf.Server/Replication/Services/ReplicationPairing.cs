using ByteShelf.Server.ChangeLog.Repositories;
using ByteShelf.Server.Replication.Models;
using ByteShelf.Server.Replication.Repositories;
using ByteShelf.Server.Shared.Auth;
using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Storage.Repositories;
using ByteShelf.Server.Storage.Services;
using ByteShelfCommon;
using EasyReasy.Database;

namespace ByteShelf.Server.Replication.Services
{
    /// <summary>
    /// Pairing a replica with a primary, both halves, and ending it. The replica pulls everything, so
    /// the primary never calls the replica: a code made on the primary carries its address and a
    /// one-time secret, the replica presents the secret, and the primary answers with the credential
    /// the replica uses from then on.
    /// </summary>
    /// <remarks>
    /// Not done (review item 55, a taste call): Storage's naming, an interface and a <c>…Service</c>
    /// name. Nothing substitutes this class, and renaming it would only move code around.
    /// </remarks>
    public sealed class ReplicationPairing
    {
        private readonly IReplicaRepository _replicaRepository;
        private readonly IReplicationSourceRepository _sourceRepository;
        private readonly IReplicationCopyRepository _copyRepository;
        private readonly IServerInstanceRepository _instanceRepository;
        private readonly IShelfRepository _shelfRepository;
        private readonly IChangeLogRepository _changeLog;
        private readonly IDbSessionFactory _sessionFactory;
        private readonly PrimaryClient _primaryClient;
        private readonly ReplicaSyncControl _syncControl;
        private readonly MaintenanceGate _maintenanceGate;
        private readonly ReplicationOptions _options;
        private readonly TimeProvider _timeProvider;

        public ReplicationPairing(
            IReplicaRepository replicaRepository,
            IReplicationSourceRepository sourceRepository,
            IReplicationCopyRepository copyRepository,
            IServerInstanceRepository instanceRepository,
            IShelfRepository shelfRepository,
            IChangeLogRepository changeLog,
            IDbSessionFactory sessionFactory,
            PrimaryClient primaryClient,
            ReplicaSyncControl syncControl,
            MaintenanceGate maintenanceGate,
            ReplicationOptions options,
            TimeProvider timeProvider)
        {
            _replicaRepository = replicaRepository;
            _sourceRepository = sourceRepository;
            _copyRepository = copyRepository;
            _instanceRepository = instanceRepository;
            _shelfRepository = shelfRepository;
            _changeLog = changeLog;
            _sessionFactory = sessionFactory;
            _primaryClient = primaryClient;
            _syncControl = syncControl;
            _maintenanceGate = maintenanceGate;
            _options = options;
            _timeProvider = timeProvider;
        }

        /// <summary>This server's part in replication, for its admins.</summary>
        public async Task<ReplicationStatus> GetStatusAsync()
        {
            long latest = await _changeLog.GetLatestSequenceAsync();
            ReplicationSource? source = await _sourceRepository.GetAsync();
            IReadOnlyList<Replica> replicas = await _replicaRepository.GetAllAsync();

            return new ReplicationStatus
            {
                LatestSequence = latest,
                Replicas = replicas.Select(replica => new ReplicaInfo
                {
                    Id = replica.Id,
                    Name = replica.Name,
                    PairedAt = replica.PairedAt,
                    LastSeenAt = replica.LastSeenAt,
                    AppliedSequence = replica.AppliedSequence,
                    ChangesBehind = Math.Max(latest - replica.AppliedSequence, 0),
                }).ToList(),
                Source = source == null ? null : new ReplicationSourceInfo
                {
                    PrimaryUrl = source.PrimaryUrl,
                    PairedAt = source.PairedAt,
                    AppliedSequence = source.AppliedSequence,
                    Copying = source.NeedsCopy,
                    LastSyncedAt = source.LastSyncedAt,
                    LastError = source.LastError,
                    LastErrorAt = source.LastErrorAt,
                },
            };
        }

        /// <summary>On a primary: a pairing code for one replica, usable once for <see cref="ReplicationOptions.CodeLifetime"/>.</summary>
        public async Task<PairingCodeResponse> CreateCodeAsync()
        {
            string secret = RandomSecret.Generate();
            DateTimeOffset expiresAt = _timeProvider.GetUtcNow() + ReplicationOptions.CodeLifetime;

            await _replicaRepository.InsertInviteAsync(Guid.NewGuid(), SecretHash.Sha256Hex(secret), expiresAt);

            PairingCode code = new PairingCode
            {
                PrimaryUrl = _options.PublicUrl,
                InstanceId = await _instanceRepository.GetInstanceIdAsync(),
                Secret = secret,
            };

            return new PairingCodeResponse { Code = code.ToString(), ExpiresAt = expiresAt };
        }

        /// <summary>On a primary: pairs the replica presenting a code's secret, and answers its credential.</summary>
        /// <exception cref="NotFoundException">No unused, unexpired code has this secret.</exception>
        public async Task<AcceptPairingResponse> AcceptAsync(AcceptPairingRequest request)
        {
            string name = DisplayText.Validate(request.Name, PairWithPrimaryRequest.MaxNameLength, "A replica's name");
            string credential = RandomSecret.Generate(ReplicaCredentials.Prefix);

            await using (IDbTransactionSession session = await _sessionFactory.CreateSessionWithTransactionAsync())
            {
                if (!await _replicaRepository.TryConsumeInviteAsync(SecretHash.Sha256Hex(request.Secret), _timeProvider.GetUtcNow(), session))
                {
                    throw new NotFoundException("That pairing code has been used or has run out.");
                }

                Replica replica = new Replica
                {
                    Id = Guid.NewGuid(),
                    Name = name,
                    PairedAt = _timeProvider.GetUtcNow(),
                    AppliedSequence = 0,
                };

                await _replicaRepository.InsertReplicaAsync(replica, SecretHash.Sha256Hex(credential), session);
                await session.CommitAsync();
            }

            return new AcceptPairingResponse { Credential = credential, InstanceId = await _instanceRepository.GetInstanceIdAsync() };
        }

        /// <summary>
        /// On a server about to become a replica: pairs with the primary a code names. Only a server with
        /// no shelves and no replicas of its own may: it takes the primary's shelves and users, its own
        /// users included, so its admins sign in with their accounts on the primary from then on.
        /// </summary>
        public async Task<ReplicationStatus> PairAsync(PairWithPrimaryRequest request, CancellationToken cancellationToken)
        {
            string name = DisplayText.Validate(request.Name, PairWithPrimaryRequest.MaxNameLength, "A replica's name");
            PairingCode code = PairingCode.TryParse(request.Code)
                ?? throw new ArgumentException("That is not a pairing code; make one on the primary.");

            if (await _sourceRepository.GetAsync() != null)
            {
                throw new ConflictException("This server is already a replica.");
            }

            if (string.Equals(code.InstanceId, await _instanceRepository.GetInstanceIdAsync(), StringComparison.Ordinal))
            {
                throw new ConflictException("That code was made on this server; paste it into the server that is to be the replica.");
            }

            if ((await _replicaRepository.GetAllAsync()).Count > 0)
            {
                throw new ConflictException("This server has replicas of its own; a replica cannot have any. Remove them first.");
            }

            if ((await _shelfRepository.GetAllAsync()).Count > 0)
            {
                throw new ConflictException("Only a server without shelves can become a replica: it takes the primary's shelves and users.");
            }

            AcceptPairingResponse accepted = await _primaryClient.AcceptAsync(
                code.PrimaryUrl,
                new AcceptPairingRequest { Secret = code.Secret, Name = name },
                cancellationToken);

            if (!string.Equals(accepted.InstanceId, code.InstanceId, StringComparison.Ordinal))
            {
                throw new PrimaryFailedException($"The server at {code.PrimaryUrl} is not the one that made the code.");
            }

            ReplicationSource source = new ReplicationSource
            {
                PrimaryUrl = code.PrimaryUrl,
                PrimaryInstanceId = code.InstanceId,
                Credential = accepted.Credential,
                PairedAt = _timeProvider.GetUtcNow(),
                AppliedSequence = 0,
                NeedsCopy = true,
            };

            if (!await _sourceRepository.TryInsertAsync(source))
            {
                throw new ConflictException("This server is already a replica.");
            }

            _syncControl.Wake();
            return await GetStatusAsync();
        }

        /// <summary>On a primary: unpairs a replica, which can no longer read the log.</summary>
        public async Task RemoveReplicaAsync(Guid id)
        {
            if (!await _replicaRepository.DeleteAsync(id))
            {
                throw new NotFoundException("There is no such replica.");
            }
        }

        /// <summary>On a primary: <paramref name="replica"/> has stopped following this server, so it is unpaired.</summary>
        public async Task ForgetReplicaAsync(Replica replica)
        {
            await _replicaRepository.DeleteAsync(replica.Id);
        }

        /// <summary>
        /// On a replica: stops following the primary, so this server takes writes and can have replicas of
        /// its own. What it holds stays as it is; a whole copy in progress is dropped. Answers whether the
        /// primary heard; when it did not, the replica stays listed there until removed.
        /// </summary>
        public async Task<bool> PromoteAsync(CancellationToken cancellationToken)
        {
            ReplicationSource source;

            // Steps paused, so one in flight stops rather than finishing a copy first; then under the
            // gate it holds while it applies, so nothing is applied after this.
            using (_syncControl.PauseSteps())
            using (await _maintenanceGate.EnterAsync(cancellationToken))
            {
                await using IDbTransactionSession session = await _sessionFactory.CreateSessionWithTransactionAsync();
                source = await _sourceRepository.GetAsync(session)
                    ?? throw new NotFoundException("This server is not a replica.");

                await _copyRepository.ClearAsync(session);
                await _sourceRepository.DeleteAsync(session);
                await session.CommitAsync();
            }

            return await _primaryClient.LeaveAsync(source, cancellationToken);
        }
    }
}
