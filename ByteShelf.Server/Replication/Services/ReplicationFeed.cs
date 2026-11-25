using ByteShelf.Server.ChangeLog.Models;
using ByteShelf.Server.ChangeLog.Repositories;
using ByteShelf.Server.Replication.Models;
using ByteShelf.Server.Replication.Repositories;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Repositories;
using ByteShelf.Server.Storage.Services;
using EasyReasy.Database;

namespace ByteShelf.Server.Replication.Services
{
    /// <inheritdoc />
    public sealed class ReplicationFeed : IReplicationFeed
    {
        private readonly IChangeLogRepository _changeLog;
        private readonly IReplicaRepository _replicaRepository;
        private readonly IServerInstanceRepository _instanceRepository;
        private readonly IFileRepository _fileRepository;
        private readonly IDbSessionFactory _sessionFactory;
        private readonly ObjectStore _objectStore;
        private readonly TimeProvider _timeProvider;

        public ReplicationFeed(
            IChangeLogRepository changeLog,
            IReplicaRepository replicaRepository,
            IServerInstanceRepository instanceRepository,
            IFileRepository fileRepository,
            IDbSessionFactory sessionFactory,
            ObjectStore objectStore,
            TimeProvider timeProvider)
        {
            _changeLog = changeLog;
            _replicaRepository = replicaRepository;
            _instanceRepository = instanceRepository;
            _fileRepository = fileRepository;
            _sessionFactory = sessionFactory;
            _objectStore = objectStore;
            _timeProvider = timeProvider;
        }

        /// <inheritdoc />
        public async Task<string?> GetObjectPathAsync(Replica replica, Guid fileId, string sha256)
        {
            await _replicaRepository.RecordSeenAsync(replica.Id, _timeProvider.GetUtcNow());
            FileRecord? file = await _fileRepository.GetByIdAsync(fileId);
            return file != null && string.Equals(file.Sha256, sha256, StringComparison.Ordinal) && _objectStore.Exists(sha256)
                ? _objectStore.GetPath(sha256)
                : null;
        }

        /// <inheritdoc />
        public async Task<ChangeFeedPage> GetChangesAsync(Replica replica, long afterSequence, string? afterFingerprint, int limit)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(afterSequence);
            await _replicaRepository.RecordFollowingAsync(replica.Id, afterSequence, _timeProvider.GetUtcNow());
            string instanceId = await _instanceRepository.GetInstanceIdAsync();

            // One read transaction: compaction committing between the reads could otherwise drop an entry
            // this page would skip without saying a copy is needed.
            await using IDbTransactionSession session = await _sessionFactory.CreateSessionWithTransactionAsync();
            long latest = await _changeLog.GetLatestSequenceAsync(session);
            long prunedThrough = await _changeLog.GetPrunedThroughAsync(session);
            bool copyNeeded = afterSequence < prunedThrough
                || afterSequence > latest
                || !await HoldsAsync(afterSequence, afterFingerprint, prunedThrough, session);

            if (copyNeeded)
            {
                return Page(instanceId, new ChangeLogPage([], HasMore: false), latest, copyNeeded: true);
            }

            return Page(instanceId, await _changeLog.GetPageAsync(afterSequence, PageLimit(limit), session), latest, copyNeeded: false);
        }

        /// <inheritdoc />
        public async Task<ChangeFeedPage> GetCopyAsync(Replica replica, long afterSequence, int limit)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(afterSequence);
            await _replicaRepository.RecordCopyingAsync(replica.Id, _timeProvider.GetUtcNow());
            string instanceId = await _instanceRepository.GetInstanceIdAsync();

            // The latest sequence and the page from one snapshot: a replica follows the log from there once
            // the copy is in, so nothing made while it reads the copy is missed.
            await using IDbTransactionSession session = await _sessionFactory.CreateSessionWithTransactionAsync();
            long latest = await _changeLog.GetLatestSequenceAsync(session);
            return Page(instanceId, await _changeLog.GetLatestStatesPageAsync(afterSequence, PageLimit(limit), session), latest, copyNeeded: false);
        }

        private static ChangeLogPageLimit PageLimit(int limit)
        {
            return new ChangeLogPageLimit(Math.Clamp(limit, 1, IReplicationFeed.MaxPageSize), IReplicationFeed.MaxPagePayloadBytes);
        }

        /// <summary>
        /// Whether the entry a replica applied last is still the one numbered <paramref name="sequence"/>
        /// here. A primary restored from a backup gives the numbers it lost to other entries; without a
        /// fingerprint (a replica fresh from a copy) there is nothing to compare. Nor at
        /// <paramref name="prunedThrough"/>, whose entry compaction dropped: a replica that applied it lacks
        /// nothing, so it follows on, and a restore is not noticed there (Replication.md, <i>Whole copies</i>).
        /// </summary>
        private async Task<bool> HoldsAsync(long sequence, string? fingerprint, long prunedThrough, IDbSession session)
        {
            if (fingerprint == null || sequence == 0 || sequence == prunedThrough)
            {
                return true;
            }

            ChangeLogEntry? entry = await _changeLog.GetBySequenceAsync(sequence, session);
            return entry != null && string.Equals(entry.GetFingerprint(), fingerprint, StringComparison.Ordinal);
        }

        private static ChangeFeedPage Page(string instanceId, ChangeLogPage page, long latest, bool copyNeeded)
        {
            return new ChangeFeedPage
            {
                InstanceId = instanceId,
                Entries = page.Entries,
                HasMore = page.HasMore,
                LatestSequence = latest,
                CopyNeeded = copyNeeded,
            };
        }
    }
}
