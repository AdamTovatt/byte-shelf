namespace ByteShelf.Server.Users.Models
{
    /// <summary>The signed-in user's passkeys, and whether this server can add one or sign in with one.</summary>
    public sealed class PasskeyList
    {
        /// <summary>False while passkeys are off: the server has no <c>APP_BASE_URL</c> a browser makes passkeys for.</summary>
        public required bool Available { get; init; }

        /// <summary>Whether a passkey can be added here: passkeys are on and the server is not a read-only replica.</summary>
        public required bool CanAdd { get; init; }

        /// <summary>The user's passkeys, oldest first.</summary>
        public required IReadOnlyList<PasskeyInfo> Passkeys { get; init; }
    }
}
