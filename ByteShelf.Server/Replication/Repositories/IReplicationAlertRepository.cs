using ByteShelf.Server.Replication.Models;
using EasyReasy.Database;

namespace ByteShelf.Server.Replication.Repositories
{
    /// <summary>The replication problems this server's admins have been emailed about. Not in the change log: each server keeps its own.</summary>
    public interface IReplicationAlertRepository
    {
        Task<IReadOnlyList<ReplicationAlert>> GetAllAsync(IDbSession? session = null);

        /// <summary>Records <paramref name="alert"/>, replacing what was recorded for its subject.</summary>
        Task SaveAsync(ReplicationAlert alert, IDbSession? session = null);

        Task DeleteAsync(string subject, IDbSession? session = null);
    }
}
