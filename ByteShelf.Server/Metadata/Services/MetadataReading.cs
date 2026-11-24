using ByteShelf.Server.Metadata.Models;
using ByteShelf.Server.Replication.Services;
using EasyReasy.Database;

namespace ByteShelf.Server.Metadata.Services
{
    /// <summary>
    /// One pass of the job <c>metadata.reading</c>: drops what was read from objects no plain file holds
    /// any more, then runs each reader in turn over every object that a live, plain file of its types holds
    /// and it has not read, whenever it was stored. Only a primary reads; a replica takes the rows from its
    /// primary's change log, so it does nothing.
    /// </summary>
    internal sealed class MetadataReading
    {
        /// <summary>How many objects one query lists to read.</summary>
        internal const int BatchSize = 100;

        private readonly ContentReaders _readers;
        private readonly ContentReadRunner _runner;
        private readonly MetadataUpkeep _upkeep;
        private readonly IDbSessionFactory _sessionFactory;
        private readonly ReplicationState _replicationState;
        private readonly TimeProvider _timeProvider;

        public MetadataReading(
            ContentReaders readers,
            ContentReadRunner runner,
            MetadataUpkeep upkeep,
            IDbSessionFactory sessionFactory,
            ReplicationState replicationState,
            TimeProvider timeProvider)
        {
            _readers = readers;
            _runner = runner;
            _upkeep = upkeep;
            _sessionFactory = sessionFactory;
            _replicationState = replicationState;
            _timeProvider = timeProvider;
        }

        public async Task<MetadataReadingSummary> RunAsync(CancellationToken cancellationToken)
        {
            if (!await _replicationState.AllowsWritesAsync())
            {
                return new MetadataReadingSummary(OnReplica: true, Read: 0, Failed: 0, Removed: 0);
            }

            int removed;
            await using (IDbTransactionSession session = await _sessionFactory.CreateSessionWithTransactionAsync())
            {
                removed = await _upkeep.RemoveUnheldAsync(_timeProvider.GetUtcNow(), session);
                await session.CommitAsync();
            }

            MetadataReadingSummary summary = new MetadataReadingSummary(OnReplica: false, Read: 0, Failed: 0, removed);
            foreach (ContentReader reader in _readers.All)
            {
                summary = await RunReaderAsync(reader, summary, cancellationToken);
            }

            return summary;
        }

        /// <summary>
        /// Reads everything <paramref name="reader"/> has yet to read, until it may not run any more; answers
        /// <paramref name="summary"/> with what it did counted in.
        /// </summary>
        private async Task<MetadataReadingSummary> RunReaderAsync(ContentReader reader, MetadataReadingSummary summary, CancellationToken cancellationToken)
        {
            // Listed after the last one tried rather than from the start, so contents that vanish
            // before they are read are passed over instead of listed again.
            string? after = null;
            while (await reader.GetUnreadAsync(after, BatchSize) is { Count: > 0 } unread)
            {
                foreach (string sha256 in unread)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!_runner.MayRun(reader))
                    {
                        return summary;
                    }

                    after = sha256;
                    if (await _runner.ReadAsync(reader, sha256, cancellationToken) is not { } outcome)
                    {
                        continue;
                    }

                    await using (IDbTransactionSession session = await _sessionFactory.CreateSessionWithTransactionAsync())
                    {
                        // False when another pass recorded them meanwhile, or no live plain file holds them any more.
                        if (await outcome.Record(sha256, _timeProvider.GetUtcNow(), session))
                        {
                            summary = summary with { Read = summary.Read + 1, Failed = summary.Failed + (outcome.Failed ? 1 : 0) };
                        }

                        await session.CommitAsync();
                    }
                }
            }

            return summary;
        }
    }
}
