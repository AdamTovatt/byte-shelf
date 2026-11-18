namespace ByteShelf.Server.Users.Models
{
    /// <summary>One of the signed-in user's passkeys, as their account settings list it.</summary>
    public sealed class PasskeyInfo
    {
        public required Guid Id { get; init; }

        /// <summary>The name the user knows it by.</summary>
        public required string Name { get; init; }

        /// <summary>When it was added.</summary>
        public required DateTimeOffset CreatedAt { get; init; }

        /// <summary>When it last signed the user in on this server; null if it never has.</summary>
        public DateTimeOffset? LastUsedAt { get; init; }
    }
}
