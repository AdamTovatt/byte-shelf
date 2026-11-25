namespace ByteShelf.Server.ChangeLog.Models
{
    /// <summary>A true-or-false field in the states an entity type's entries hold.</summary>
    /// <param name="EntityType">The entity type.</param>
    /// <param name="JsonPath">Where the field is in a state, as <see cref="ChangePayload"/> writes it, for SQLite's <c>json_extract</c>.</param>
    public sealed record StateFlag(string EntityType, string JsonPath);
}
