namespace ByteShelf.Server.Users.Models
{
    /// <summary>Begins adding a passkey to the signed-in user, who gives their password for it.</summary>
    public sealed class BeginAddingPasskeyRequest
    {
        public required string CurrentPassword { get; init; }
    }
}
