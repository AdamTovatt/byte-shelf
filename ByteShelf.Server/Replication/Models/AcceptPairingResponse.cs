namespace ByteShelf.Server.Replication.Models
{
    /// <summary>The primary's answer to a pairing: the credential the replica presents from now on.</summary>
    public class AcceptPairingResponse
    {
        public required string Credential { get; init; }

        public required string InstanceId { get; init; }
    }
}
