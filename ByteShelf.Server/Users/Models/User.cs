namespace ByteShelf.Server.Users.Models
{
    /// <summary>
    /// A user identity: email is the stable login identifier and reset channel, the display name
    /// is what the UI shows. Never carries credential material.
    /// </summary>
    public class User
    {
        public required Guid Id { get; init; }
        public required string Email { get; init; }
        public required string DisplayName { get; init; }
        public required IReadOnlyList<string> Roles { get; init; }
        public required DateTimeOffset CreatedAt { get; init; }
        public required DateTimeOffset UpdatedAt { get; init; }

        /// <summary>
        /// When a password was last set for them without the current one, by a reset link or the server's
        /// <c>--reset-password</c>: an access token issued before then is refused. Null until then.
        /// </summary>
        public DateTimeOffset? SessionsEndedAt { get; init; }
    }
}
