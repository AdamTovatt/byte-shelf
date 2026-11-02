using ByteShelfCommon;

namespace ByteShelf.Tools
{
    /// <summary>A replica named the way an admin types it, for the CLI's and the server's own replication commands.</summary>
    public static class Replicas
    {
        /// <summary>
        /// The replica <paramref name="idOrName"/> names: the one with that id, or the only one with that
        /// name, exactly. When none or several do, the refusal says so, pointing at
        /// <paramref name="listedBy"/>, the command that lists them; the caller throws it as its kind of error.
        /// </summary>
        public static ReplicaLookup Find(IReadOnlyList<ReplicaInfo> replicas, string idOrName, string listedBy)
        {
            List<ReplicaInfo> found = Guid.TryParse(idOrName, out Guid id)
                ? replicas.Where(replica => replica.Id == id).ToList()
                : replicas.Where(replica => string.Equals(replica.Name, idOrName, StringComparison.Ordinal)).ToList();
            return found.Count switch
            {
                0 => new ReplicaLookup(null, $"No replica is called or has the id '{idOrName}'; {listedBy} lists them.", Ambiguous: false),
                1 => new ReplicaLookup(found[0], null, Ambiguous: false),
                _ => new ReplicaLookup(null, $"{found.Count} replicas are called '{idOrName}'; name one by its id, which {listedBy} lists.", Ambiguous: true),
            };
        }
    }

    /// <summary>What <see cref="Replicas.Find"/> found: the replica, or why not, and whether that is because several share the name.</summary>
    public sealed record ReplicaLookup(ReplicaInfo? Found, string? Refusal, bool Ambiguous);
}
