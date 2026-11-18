namespace ByteShelf.Server.Users.Models
{
    /// <summary>
    /// A <c>passkey</c> row as the change log carries it: a passkey a user signs in with, which a replica
    /// holds too. Its sign count and last use are the signing-in server's own (<see cref="StoredPasskey"/>).
    /// </summary>
    public class Passkey
    {
        public required Guid Id { get; init; }
        public required Guid UserId { get; init; }

        /// <summary>The credential id, base64url as the browser reports it; unique across all users.</summary>
        public required string CredentialId { get; init; }

        /// <summary>The COSE public key, base64url.</summary>
        public required string PublicKey { get; init; }

        /// <summary>The transports registration reported, handed back when the user adds another passkey; null when none were.</summary>
        public IReadOnlyList<string>? Transports { get; init; }

        public required string Name { get; init; }
        public required DateTimeOffset CreatedAt { get; init; }
    }
}
