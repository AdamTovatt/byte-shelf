namespace ByteShelf.Server.Replication.Services
{
    /// <summary>
    /// The credential a replica presents to its primary under <see cref="HeaderName"/>, never as a bearer
    /// token or shelf key, so nothing that signs in users or keys ever reads it. Made by
    /// <see cref="Shared.Auth.RandomSecret"/> with <see cref="Prefix"/>; the primary keeps only its hash.
    /// </summary>
    public static class ReplicaCredentials
    {
        public const string HeaderName = "X-Replica-Key";

        public const string Prefix = "bsr_";
    }
}
