using ByteShelf.Server.ChangeLog.Models;
using ByteShelf.Server.Replication.Models;
using ByteShelf.Server.Replication.Repositories;
using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Services;
using ByteShelf.Server.Users.Services;
using EasyReasy.Database;

namespace ByteShelf.Server.Replication.Services
{
    /// <summary>
    /// A replica's side of following its primary: a whole copy at first (and after falling too far
    /// behind), then the log a page at a time. Each page is applied in one transaction with how far it
    /// reaches, so a replica stopped anywhere picks up where it was. An object a file names is fetched,
    /// and its hash checked, before the file's row is written, so no row here ever names a missing object.
    /// </summary>
    /// <remarks>
    /// Every step runs under <see cref="MaintenanceGate"/>, which storage maintenance holds too: objects
    /// fetched but not yet named by a committed row are never swept, nor are their staging files. So
    /// maintenance on a replica waits while a whole copy is taken. On a replica nothing else removes
    /// objects, since every write that could is refused. A promotion pauses the steps
    /// (<see cref="ReplicaSyncControl"/>), stopping one in flight rather than waiting for it.
    /// </remarks>
    public sealed class ReplicaSync
    {
        private readonly IReplicationSourceRepository _sourceRepository;
        private readonly IReplicationCopyRepository _copyRepository;
        private readonly IDbSessionFactory _sessionFactory;
        private readonly PrimaryClient _primaryClient;
        private readonly ReplicaApplier _applier;
        private readonly ObjectStore _objectStore;
        private readonly StagingArea _stagingArea;
        private readonly MaintenanceGate _maintenanceGate;
        private readonly ReplicaSyncControl _control;
        private readonly IUserService _userService;
        private readonly int _pageSize;
        private readonly TimeProvider _timeProvider;

        public ReplicaSync(
            IReplicationSourceRepository sourceRepository,
            IReplicationCopyRepository copyRepository,
            IDbSessionFactory sessionFactory,
            PrimaryClient primaryClient,
            ReplicaApplier applier,
            ObjectStore objectStore,
            StagingArea stagingArea,
            MaintenanceGate maintenanceGate,
            ReplicaSyncControl control,
            IUserService userService,
            ReplicationOptions options,
            TimeProvider timeProvider)
        {
            _sourceRepository = sourceRepository;
            _copyRepository = copyRepository;
            _sessionFactory = sessionFactory;
            _primaryClient = primaryClient;
            _applier = applier;
            _objectStore = objectStore;
            _stagingArea = stagingArea;
            _maintenanceGate = maintenanceGate;
            _control = control;
            _userService = userService;
            _pageSize = options.PageSize;
            _timeProvider = timeProvider;
        }

        /// <summary>
        /// Takes one step: the whole copy when one is due, else one page of the log. Answers whether this
        /// server has caught up, so there is nothing to do until the primary changes something. Does
        /// nothing on a server that is not a replica, nor while steps are paused for a promotion.
        /// </summary>
        public async Task<bool> SyncOnceAsync(CancellationToken cancellationToken)
        {
            CancellationToken paused = _control.StepToken;
            using CancellationTokenSource step = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, paused);

            try
            {
                using (await _maintenanceGate.EnterAsync(step.Token))
                {
                    return await StepAsync(step.Token);
                }
            }
            catch (OperationCanceledException) when (paused.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                // Stopped for a promotion, which takes over from here.
                return true;
            }
        }

        /// <summary>Records why the last step failed, for the replica's admins to see.</summary>
        public async Task RecordFailureAsync(Exception exception)
        {
            // Only messages worded here for admins; anything else may carry internals.
            string message = exception is PrimaryFailedException or ReplicaOutdatedException
                ? exception.Message
                : "Replication failed; the server's log says why.";

            await _sourceRepository.RecordSyncErrorAsync(message, _timeProvider.GetUtcNow());
        }

        private async Task<bool> StepAsync(CancellationToken cancellationToken)
        {
            ReplicationSource? source = await _sourceRepository.GetAsync();
            if (source == null)
            {
                return true;
            }

            if (source.NeedsCopy)
            {
                await CopyAsync(source, cancellationToken);
                return false;
            }

            ChangeFeedPage page = await _primaryClient.GetChangesAsync(source, source.AppliedSequence, source.AppliedFingerprint, _pageSize, cancellationToken);
            if (page.CopyNeeded)
            {
                await _sourceRepository.RequireCopyAsync();
                return false;
            }

            ChangeLogEntry? last = page.Entries.Count > 0 ? page.Entries[^1] : null;
            await ApplyAsync(
                source,
                page.Entries,
                last?.Sequence ?? source.AppliedSequence,
                last?.GetFingerprint() ?? source.AppliedFingerprint,
                cancellationToken);

            return !page.HasMore || last == null;
        }

        private async Task ApplyAsync(ReplicationSource source, IReadOnlyList<ChangeLogEntry> entries, long reached, string? reachedFingerprint, CancellationToken cancellationToken)
        {
            HashSet<Guid> purged = new HashSet<Guid>();
            await FetchObjectsAsync(source, entries, purged, cancellationToken);

            IReadOnlyList<Guid> removedUsers;
            await using (IDbTransactionSession session = await _sessionFactory.CreateSessionWithTransactionAsync())
            {
                removedUsers = await _applier.ApplyAsync(WithoutPurged(entries, purged), session);
                await _sourceRepository.SetProgressAsync(reached, reachedFingerprint, _timeProvider.GetUtcNow(), session);
                await session.CommitAsync();
            }

            await EndSessionsAsync(removedUsers);
        }

        /// <summary>
        /// Reads the primary's whole state into <c>replication_copy</c>, fetches the objects it names, then
        /// in one transaction removes what the primary no longer has and writes everything it does. Each
        /// page is read at its own moment, not from one snapshot: an entity that changes after its page
        /// comes again on a later one, as its newer state or its removal, which replaces or drops what was
        /// copied, so the copy ends as the primary stood at its last page. The log is then followed from
        /// where it stood when the copy began, so a change made while the copy was read is applied after
        /// it, whatever page it fell on.
        /// </summary>
        private async Task CopyAsync(ReplicationSource source, CancellationToken cancellationToken)
        {
            await _copyRepository.ClearAsync();

            long? startedAt = null;
            long after = 0;
            HashSet<Guid> purged = new HashSet<Guid>();
            while (true)
            {
                ChangeFeedPage page = await _primaryClient.GetCopyAsync(source, after, _pageSize, cancellationToken);
                startedAt ??= page.LatestSequence;

                await using (IDbTransactionSession session = await _sessionFactory.CreateSessionWithTransactionAsync())
                {
                    await _copyRepository.AddAsync(page.Entries, session);
                    await session.CommitAsync();
                }

                await FetchObjectsAsync(source, page.Entries, purged, cancellationToken);

                if (!page.HasMore || page.Entries.Count == 0)
                {
                    break;
                }

                after = page.Entries[^1].Sequence;
            }

            List<Guid> removedUsers = new List<Guid>();
            await using (IDbTransactionSession session = await _sessionFactory.CreateSessionWithTransactionAsync())
            {
                removedUsers.AddRange(await _applier.RemoveMissingFromCopyAsync(session));

                long applied = 0;
                while (true)
                {
                    IReadOnlyList<ChangeLogEntry> copied = await _copyRepository.GetAfterAsync(applied, _pageSize, session);
                    if (copied.Count == 0)
                    {
                        break;
                    }

                    removedUsers.AddRange(await _applier.ApplyAsync(WithoutPurged(copied, purged), session));
                    applied = copied[^1].Sequence;
                }

                // No fingerprint: the entry at startedAt may not be in the copy, which holds only the newest
                // state of each entity. The next page applied gives one.
                await _copyRepository.ClearAsync(session);
                await _sourceRepository.SetProgressAsync(startedAt.Value, appliedFingerprint: null, _timeProvider.GetUtcNow(), session);
                await session.CommitAsync();
            }

            await EndSessionsAsync(removedUsers);
        }

        /// <summary>
        /// Fetches every object the files in <paramref name="entries"/> name that is not here yet, checking
        /// each one's hash, and adds to <paramref name="purged"/> the files the primary has purged since:
        /// their bytes are gone there, so their entries are not applied, and a removal follows in the log.
        /// </summary>
        private async Task FetchObjectsAsync(ReplicationSource source, IReadOnlyList<ChangeLogEntry> entries, ISet<Guid> purged, CancellationToken cancellationToken)
        {
            IEnumerable<IGrouping<string, Guid>> filesByObject = entries
                .Where(entry => entry.EntityType == ChangeEntityType.File && entry.Payload != null)
                .GroupBy(entry => ChangePayload.Deserialize<FileRecord>(entry.Payload!).Sha256, entry => entry.EntityId, StringComparer.Ordinal);

            foreach (IGrouping<string, Guid> files in filesByObject)
            {
                if (!ObjectStore.IsValidHash(files.Key))
                {
                    throw new PrimaryFailedException($"The primary at {source.PrimaryUrl} named a file's contents by '{files.Key}', which is not a SHA-256.");
                }

                if (_objectStore.Exists(files.Key))
                {
                    continue;
                }

                bool fetched = false;
                foreach (Guid fileId in files.Distinct())
                {
                    if (await FetchObjectAsync(source, fileId, files.Key, cancellationToken))
                    {
                        fetched = true;
                        break;
                    }
                }

                if (!fetched)
                {
                    purged.UnionWith(files);
                }
            }
        }

        /// <summary>The entries to apply: all but the writes of files whose bytes the primary no longer has.</summary>
        private static IReadOnlyList<ChangeLogEntry> WithoutPurged(IReadOnlyList<ChangeLogEntry> entries, IReadOnlySet<Guid> purged)
        {
            return purged.Count == 0
                ? entries
                : entries.Where(entry => !(entry.EntityType == ChangeEntityType.File && entry.Payload != null && purged.Contains(entry.EntityId))).ToList();
        }

        /// <summary>Fetches one file's bytes as <paramref name="sha256"/>; false when the primary has purged the file.</summary>
        private async Task<bool> FetchObjectAsync(ReplicationSource source, Guid fileId, string sha256, CancellationToken cancellationToken)
        {
            Guid stagingId = Guid.NewGuid();
            try
            {
                await using (FileStream file = new FileStream(_stagingArea.GetPath(stagingId), FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 1, FileOptions.Asynchronous))
                {
                    if (!await _primaryClient.DownloadObjectAsync(source, fileId, file, cancellationToken))
                    {
                        return false;
                    }

                    await file.FlushAsync(cancellationToken);
                    file.Flush(flushToDisk: true);
                }

                string received = await _stagingArea.ComputeSha256Async(stagingId, cancellationToken);
                if (!string.Equals(received, sha256, StringComparison.Ordinal))
                {
                    throw new PrimaryFailedException($"A file from the primary at {source.PrimaryUrl} did not match its SHA-256 ({sha256}), so it was not kept.");
                }

                using (await _objectStore.LockAsync(sha256, cancellationToken))
                {
                    _objectStore.Place(_stagingArea.GetPath(stagingId), sha256);
                }

                return true;
            }
            finally
            {
                _stagingArea.Delete(stagingId);
            }
        }

        private async Task EndSessionsAsync(IReadOnlyList<Guid> removedUsers)
        {
            foreach (Guid userId in removedUsers)
            {
                await _userService.EndSessionsAsync(userId);
            }
        }
    }
}
