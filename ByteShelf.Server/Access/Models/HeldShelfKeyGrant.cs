namespace ByteShelf.Server.Access.Models
{
    /// <summary>A key's grant with the name of the shelf it is on, read together, for listing a key's shelves.</summary>
    public class HeldShelfKeyGrant
    {
        public required ShelfKeyGrant Grant { get; init; }
        public required string ShelfName { get; init; }
    }
}
