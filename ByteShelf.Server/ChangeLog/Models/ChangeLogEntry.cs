using System.Globalization;

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

        /// <summary>
        /// Tells this entry from another given the same sequence, as by a primary restored from a backup
        /// that reuses the numbers it lost: the entity and the instant, to the tick.
        /// </summary>
        public string GetFingerprint()
        {
            return string.Create(CultureInfo.InvariantCulture, $"{EntityType}:{EntityId:N}:{CreatedAt.UtcTicks}");
        }
    }
}
