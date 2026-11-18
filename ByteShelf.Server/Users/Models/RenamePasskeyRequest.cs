namespace ByteShelf.Server.Users.Models
{
    /// <summary>Renames one of the signed-in user's passkeys.</summary>
    public sealed class RenamePasskeyRequest
    {
        public required string Name { get; init; }
    }
}
