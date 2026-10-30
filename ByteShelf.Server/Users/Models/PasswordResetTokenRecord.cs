namespace ByteShelf.Server.Users.Models
{
    /// <summary>
    /// A stored password-reset token row. Only the SHA-256 hash of the token is persisted.
    /// </summary>
    public class PasswordResetTokenRecord
    {
        public required Guid Id { get; init; }
        public required Guid UserId { get; init; }
        public required string TokenHash { get; init; }
        public required DateTimeOffset CreatedAt { get; init; }
        public required DateTimeOffset ExpiresAt { get; init; }
        public DateTimeOffset? UsedAt { get; init; }
    }
}
