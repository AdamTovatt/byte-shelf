using System.Text.Json.Serialization;

namespace ByteShelf.Server.Access.Models
{
    /// <summary>
    /// A <c>shelf_key</c> row: a program's key, stored as the SHA-256 of its secret. The shelves it works
    /// on are its <see cref="ShelfKeyGrant"/>s.
    /// </summary>
    /// <remarks>
    /// Unknown members are refused, so a change-log entry in a shape this version does not know, as a
    /// primary on another version sends, fails to apply instead of writing a key that works on no shelf.
    /// </remarks>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public class ShelfKey
    {
        public required Guid Id { get; init; }
        public required string Name { get; init; }
        public required string SecretSha256 { get; init; }
        public required DateTimeOffset CreatedAt { get; init; }
    }
}
