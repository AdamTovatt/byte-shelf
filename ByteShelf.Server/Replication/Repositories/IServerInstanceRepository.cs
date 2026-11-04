using EasyReasy.Database;

namespace ByteShelf.Server.Replication.Repositories
{
    /// <summary>Who this server is: a pairing code and every feed page name the instance that made them.</summary>
    public interface IServerInstanceRepository
    {
        /// <summary>This server's instance id, made the first time it is asked for.</summary>
        Task<string> GetInstanceIdAsync(IDbSession? session = null);
    }
}
