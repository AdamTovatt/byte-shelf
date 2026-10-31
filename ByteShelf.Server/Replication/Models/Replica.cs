namespace ByteShelf.Server.Replication.Models
{
    /// <summary>On a primary: a paired replica, and how far it last said it had applied the change log.</summary>
    public class Replica
    {
        public required Guid Id { get; init; }

        /// <summary>The name its admin gave it when pairing.</summary>
        public required string Name { get; init; }

        public required DateTimeOffset PairedAt { get; init; }

        public DateTimeOffset? LastSeenAt { get; init; }

        public required long AppliedSequence { get; init; }
    }
}
