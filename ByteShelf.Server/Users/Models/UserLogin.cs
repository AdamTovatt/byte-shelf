namespace ByteShelf.Server.Users.Models
{
    /// <summary>
    /// A <c>user_login</c> row: how a user proves who they are with one provider. For the local
    /// provider, <see cref="Credential"/> is the password hash. Only the change log reads it whole, so a
    /// replica holds what it needs to let users sign in once it is promoted.
    /// </summary>
    public class UserLogin
    {
        public required Guid Id { get; init; }
        public required Guid UserId { get; init; }
        public required string Provider { get; init; }
        public required string ProviderUserId { get; init; }
        public string? Credential { get; init; }
        public required DateTimeOffset CreatedAt { get; init; }
    }
}
