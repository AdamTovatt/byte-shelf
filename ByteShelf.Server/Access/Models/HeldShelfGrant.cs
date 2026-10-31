namespace ByteShelf.Server.Access.Models
{
    /// <summary>A grant with the email and display name of the user holding it, for listing a shelf's access.</summary>
    public class HeldShelfGrant
    {
        public required ShelfGrant Grant { get; init; }
        public required string Email { get; init; }
        public required string DisplayName { get; init; }
    }
}
