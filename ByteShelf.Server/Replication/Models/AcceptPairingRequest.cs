namespace ByteShelf.Server.Replication.Models
{
    /// <summary>What a replica sends the primary to pair, with the secret from the pairing code.</summary>
    public class AcceptPairingRequest
    {
        public required string Secret { get; init; }

        public required string Name { get; init; }
    }
}
