using ByteShelf.Server.Metadata.Models;
using ByteShelf.Server.Metadata.Repositories;
using ByteShelf.Server.Replication.Services;
using EasyReasy.Database;

namespace ByteShelf.Server.Metadata.Services
{
    /// <summary>
    /// One pass of the job <c>metadata.reading</c>: drops what any reader read from objects no plain file holds
    /// any more, then runs the server's own. Only a primary reads; a replica takes the rows from its primary's
    /// change log, so it does nothing.
    /// </summary>
    internal sealed class MetadataReading
    {
        private readonly IObjectMetadataRepository _objectMetadata;
        private readonly ContentReading _reading;
        private readonly MetadataUpkeep _upkeep;
        private readonly IDbSessionFactory _sessionFactory;
        private readonly ReplicationState _replicationState;
        private readonly TimeProvider _timeProvider;

        public MetadataReading(
            IObjectMetadataRepository objectMetadata,
            ContentReading reading,
            MetadataUpkeep upkeep,
            IDbSessionFactory sessionFactory,
            ReplicationState replicationState,
            TimeProvider timeProvider)
        {
            _objectMetadata = objectMetadata;
            _reading = reading;
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

            (int read, int failed) = await _reading.RunAsync(new BuiltInContentReader(_objectMetadata), cancellationToken);
            return new MetadataReadingSummary(OnReplica: false, read, failed, removed);
        }
    }
}
