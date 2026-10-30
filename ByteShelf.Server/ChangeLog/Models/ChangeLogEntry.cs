namespace ByteShelf.Server.ChangeLog.Models
{
    /// <summary>
    /// One recorded change: the entity's type and id, what was done, and the entity's whole state
    /// afterwards as JSON, or null once the entity is gone.
    /// </summary>
    public class ChangeLogEntry
    {
        public required long Sequence { get; init; }
        public required string EntityType { get; init; }
        public required Guid EntityId { get; init; }
        public required string Action { get; init; }
        public string? Payload { get; init; }
        public required DateTimeOffset CreatedAt { get; init; }
    }
}
