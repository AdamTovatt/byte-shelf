using ByteShelf.Server.Replication.Repositories;

namespace ByteShelf.Server.Replication.Services
{
    /// <summary>
    /// What replication means for the rest of the server: whether it takes writes. Ask here rather than
    /// looking for a <c>replication_source</c> row, so the rule has one home.
    /// </summary>
    public sealed class ReplicationState
    {
        private readonly IReplicationSourceRepository _sourceRepository;

        public ReplicationState(IReplicationSourceRepository sourceRepository)
        {
            _sourceRepository = sourceRepository;
        }

        /// <summary>Whether this server takes writes: false on a replica, which takes changes only from its primary.</summary>
        public async Task<bool> AllowsWritesAsync()
        {
            return await _sourceRepository.GetAsync() == null;
        }
    }
}
