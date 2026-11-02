using ByteShelf.Server.Replication.Repositories;
using ByteShelf.Server.Shared.Errors;

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

        /// <summary>Throws on a replica, for a write its API or the server's own commands would make.</summary>
        /// <exception cref="ReadOnlyReplicaException">This server is a replica.</exception>
        public async Task RequireWritesAsync()
        {
            if (!await AllowsWritesAsync())
            {
                throw new ReadOnlyReplicaException();
            }
        }
    }
}
