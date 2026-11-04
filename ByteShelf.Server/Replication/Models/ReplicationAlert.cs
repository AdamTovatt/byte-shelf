namespace ByteShelf.Server.Replication.Models
{
    /// <summary>A replication problem this server has emailed its admins about, and not yet seen clear.</summary>
    public sealed class ReplicationAlert
    {
        /// <summary>What it concerns: <see cref="ForReplica"/> on a primary, <see cref="Primary"/> on a replica.</summary>
        public required string Subject { get; init; }

        /// <summary>What is wrong, as last seen, which the email saying it cleared quotes.</summary>
        public required string Problem { get; init; }

        /// <summary>When the problem began, which the email saying it cleared gives.</summary>
        public required DateTimeOffset Since { get; init; }

        /// <summary>When an email about it was last sent.</summary>
        public required DateTimeOffset LastSentAt { get; init; }

        /// <summary>The subject of a replica's problems, on its primary.</summary>
        public static string ForReplica(Guid replicaId)
        {
            return $"replica:{replicaId:D}";
        }

        /// <summary>The subject of a replica's problems following its primary, on the replica.</summary>
        public const string Primary = "primary";
    }
}
