using System.Security.Cryptography;
using ByteShelfCommon;

namespace ByteShelfClient.InMemory
{
    /// <summary>
    /// An in-memory server's part in replication, by the server's rules but without data: the codes it
    /// made, the replicas following it, and the primary it follows. No change log is kept, so every
    /// sequence is 0, a replica is never behind, and it counts as seen and caught up from the moment it
    /// paired. Called under its store's <see cref="InMemoryStore.Gate"/>. Following and promoting reach
    /// the primary's store too, taking its gate inside the follower's; nothing takes them the other way
    /// round, so two stores never wait on each other.
    /// </summary>
    internal sealed class InMemoryReplication
    {
        /// <summary>What a code an in-memory client makes starts with; a server's code is refused.</summary>
        private const string CodePrefix = "bsmem1_";

        private const string CodeNotAccepted = "The primary did not accept the code: it has been used or has run out. Make a new one there.";

        /// <summary>Every in-memory primary with a code out, by the code's secret, so a follower in the same process finds it.</summary>
        private static readonly Dictionary<string, WeakReference<InMemoryStore>> PrimariesBySecret = new Dictionary<string, WeakReference<InMemoryStore>>(StringComparer.Ordinal);

        private readonly InMemoryStore _store;
        private readonly List<Code> _codes = new List<Code>();
        private readonly List<ReplicaRow> _replicas = new List<ReplicaRow>();
        private SourceRow? _source;

        public InMemoryReplication(InMemoryStore store)
        {
            _store = store;
        }

        /// <summary>Whether this store follows a primary, and so refuses writes.</summary>
        public bool IsReplica => _source != null;

        public ReplicationStatus Status()
        {
            return new ReplicationStatus
            {
                LatestSequence = 0,
                Replicas = _replicas
                    .Select(replica => new ReplicaInfo { Id = replica.Id, Name = replica.Name, PairedAt = replica.PairedAt, LastSeenAt = replica.PairedAt, AppliedSequence = 0, ChangesBehind = 0 })
                    .ToList(),
                Source = _source == null ? null : new ReplicationSourceInfo
                {
                    PrimaryUrl = _source.PrimaryAddress,
                    PairedAt = _source.PairedAt,
                    AppliedSequence = 0,
                    Copying = false,
                    LastSyncedAt = _source.PairedAt,
                },
            };
        }

        /// <summary>A new code, which a follower in this process can find until it is used or runs out.</summary>
        public PairingCodeResponse NewCode()
        {
            string secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            Code code = new Code(secret, _store.Now + PairingCodeResponse.Lifetime);
            _codes.Add(code);
            lock (PrimariesBySecret)
            {
                PrimariesBySecret[secret] = new WeakReference<InMemoryStore>(_store);
            }

            return new PairingCodeResponse { Code = CodePrefix + secret, ExpiresAt = code.ExpiresAt };
        }

        /// <summary>Follows the primary that made <paramref name="request"/>'s code, by the checks the server makes, in its order.</summary>
        public ReplicationStatus Follow(PairWithPrimaryRequest request)
        {
            string name = InMemoryErrors.Checked(() => DisplayText.Validate(request.Name, PairWithPrimaryRequest.MaxNameLength, "A replica's name"));
            string? code = request.Code?.Trim();
            if (code == null || !code.StartsWith(CodePrefix, StringComparison.Ordinal))
            {
                throw InMemoryErrors.Invalid("That is not a pairing code; make one on the primary.");
            }

            string secret = code[CodePrefix.Length..];
            if (_codes.Any(made => made.Secret == secret))
            {
                throw InMemoryErrors.Conflict("That code was made on this server; paste it into the server that is to be the replica.");
            }

            if (_replicas.Count > 0)
            {
                throw InMemoryErrors.Conflict("This server has replicas of its own; a replica cannot have any. Remove them first.");
            }

            if (_store.Shelves.Count > 0)
            {
                throw InMemoryErrors.Conflict("Only a server without shelves can become a replica: it takes the primary's shelves and users.");
            }

            InMemoryStore primary = TakePrimary(secret) ?? throw InMemoryErrors.Conflict(CodeNotAccepted);
            Guid replicaId = Guid.NewGuid();
            lock (primary.Gate)
            {
                primary.Replication.Accept(secret, replicaId, name);
            }

            _source = new SourceRow(new WeakReference<InMemoryStore>(primary), primary.Address, replicaId, _store.Now);
            return Status();
        }

        /// <exception cref="ByteShelfException"><see cref="ErrorCodes.NotFound"/>: no replica has <paramref name="replicaId"/>.</exception>
        public void RemoveReplica(Guid replicaId)
        {
            if (_replicas.RemoveAll(replica => replica.Id == replicaId) == 0)
            {
                throw InMemoryErrors.NotFound("There is no such replica.");
            }
        }

        /// <summary>Stops following the primary, telling it when it is still in this process.</summary>
        public PromoteResponse Promote()
        {
            SourceRow source = _source ?? throw InMemoryErrors.NotFound("This server is not a replica.");
            _source = null;
            if (!source.Primary.TryGetTarget(out InMemoryStore? primary))
            {
                return new PromoteResponse { PrimaryTold = false };
            }

            lock (primary.Gate)
            {
                primary.Replication._replicas.RemoveAll(replica => replica.Id == source.ReplicaId);
            }

            return new PromoteResponse { PrimaryTold = true };
        }

        /// <summary>The primary that made the code with <paramref name="secret"/>, taking it out of the lookup; null when none in this process did.</summary>
        private static InMemoryStore? TakePrimary(string secret)
        {
            lock (PrimariesBySecret)
            {
                if (!PrimariesBySecret.Remove(secret, out WeakReference<InMemoryStore>? primary))
                {
                    return null;
                }

                return primary.TryGetTarget(out InMemoryStore? store) ? store : null;
            }
        }

        /// <summary>On the primary: uses up the code with <paramref name="secret"/>, if it is still good, and lists the replica.</summary>
        private void Accept(string secret, Guid replicaId, string name)
        {
            if (_codes.RemoveAll(made => made.Secret == secret && made.ExpiresAt > _store.Now) == 0)
            {
                throw InMemoryErrors.Conflict(CodeNotAccepted);
            }

            _replicas.Add(new ReplicaRow(replicaId, name, _store.Now));
        }

        private sealed record Code(string Secret, DateTimeOffset ExpiresAt);

        private sealed record ReplicaRow(Guid Id, string Name, DateTimeOffset PairedAt);

        /// <summary>The primary followed, held weakly: a primary gone from the process cannot hear its replica leave.</summary>
        private sealed record SourceRow(WeakReference<InMemoryStore> Primary, string PrimaryAddress, Guid ReplicaId, DateTimeOffset PairedAt);
    }
}
