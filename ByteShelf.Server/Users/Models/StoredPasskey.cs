namespace ByteShelf.Server.Users.Models
{
    /// <summary>
    /// A passkey with what this server alone knows of its use: the sign count it last verified and when
    /// it last signed someone in here. Neither is logged, so a replica counts its own sign-ins.
    /// </summary>
    public class StoredPasskey
    {
        public required Passkey Passkey { get; init; }
        public required uint SignCount { get; init; }
        public DateTimeOffset? LastUsedAt { get; init; }
    }
}
