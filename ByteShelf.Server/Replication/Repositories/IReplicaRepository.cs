using ByteShelf.Server.Replication.Models;
using EasyReasy.Database;

namespace ByteShelf.Server.Replication.Repositories
{
    /// <summary>
    /// A primary's side of replication: its pairing codes and its replicas. Not in the change log: each
    /// server keeps its own.
    /// </summary>
    public interface IReplicaRepository
    {
        Task InsertInviteAsync(Guid id, string secretSha256, DateTimeOffset expiresAt, IDbSession? session = null);

        /// <summary>Uses up the code with this secret's hash if it has not run out; false otherwise. Codes that ran out go too.</summary>
        Task<bool> TryConsumeInviteAsync(string secretSha256, DateTimeOffset now, IDbSession session);

        Task InsertReplicaAsync(Replica replica, string credentialSha256, IDbSession session);

        Task<Replica?> GetByCredentialAsync(string credentialSha256, IDbSession? session = null);

        Task<IReadOnlyList<Replica>> GetAllAsync(IDbSession? session = null);

        /// <summary>The replica asked for changes at <paramref name="at"/>, having applied the log up to <paramref name="appliedSequence"/>.</summary>
        Task RecordFollowingAsync(Guid id, long appliedSequence, DateTimeOffset at, IDbSession? session = null);

        /// <summary>The replica asked for a page of a whole copy at <paramref name="at"/>.</summary>
        Task RecordCopyingAsync(Guid id, DateTimeOffset at, IDbSession? session = null);

        /// <summary>The replica asked for something else at <paramref name="at"/>, such as a file's bytes.</summary>
        Task RecordSeenAsync(Guid id, DateTimeOffset at, IDbSession? session = null);

        /// <summary>Unpairs a replica; false when there is no such replica.</summary>
        Task<bool> DeleteAsync(Guid id, IDbSession? session = null);
    }
}
