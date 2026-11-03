using ByteShelf.Server.Replication.Models;
using EasyReasy.Database;

namespace ByteShelf.Server.Replication.Repositories
{
    /// <summary>A replica's side of replication: the primary it follows and how far it has got.</summary>
    public interface IReplicationSourceRepository
    {
        /// <summary>The primary this server follows, or null when it is not a replica.</summary>
        Task<ReplicationSource?> GetAsync(IDbSession? session = null);

        /// <summary>Makes this server a replica of <paramref name="source"/>, unless it already is one; false if it is.</summary>
        Task<bool> TryInsertAsync(ReplicationSource source, IDbSession? session = null);

        /// <summary>
        /// Records how far the log is applied, in the transaction that applied it, and that no copy is due.
        /// <paramref name="appliedFingerprint"/> is the fingerprint of the entry at <paramref name="appliedSequence"/>, when known.
        /// Answers false, writing nothing, when this server no longer follows <paramref name="source"/>:
        /// promoted, or paired anew, since the step read it, perhaps by the server's command in another
        /// process. The caller then rolls back what it applied from that source.
        /// </summary>
        Task<bool> SetProgressAsync(ReplicationSource source, long appliedSequence, string? appliedFingerprint, DateTimeOffset syncedAt, IDbSession session);

        /// <summary>
        /// Records why a step failed at <paramref name="at"/>; <see cref="ReplicationSource.FailingSince"/>
        /// keeps the first such time until a step works again.
        /// </summary>
        Task RecordSyncErrorAsync(string message, DateTimeOffset at, IDbSession? session = null);

        /// <summary>Asks for a whole copy before following the log again.</summary>
        Task RequireCopyAsync(IDbSession? session = null);

        /// <summary>Stops following the primary: this server is no longer a replica. False when it was not one.</summary>
        Task<bool> DeleteAsync(IDbSession? session = null);
    }
}
