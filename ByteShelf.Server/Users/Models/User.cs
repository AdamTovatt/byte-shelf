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
    }
}
