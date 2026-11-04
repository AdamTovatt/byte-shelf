namespace ByteShelf.Server.Access.Models
{
    /// <summary>Whether a caller is a user or a key.</summary>
    public enum ShelfCallerKind
    {
        User = 0,
        Key = 1,
    }

    /// <summary>
    /// Names a caller to find again later, as an upload link does for whoever made it:
    /// <c>ShelfCallers.FindAsync</c> answers the caller as it is then, or null once it is gone.
    /// </summary>
    public sealed record ShelfCallerReference(ShelfCallerKind Kind, Guid Id);
}
