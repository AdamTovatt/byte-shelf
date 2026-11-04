using ByteShelfCommon;

namespace ByteShelf.Server.Storage.Models
{
    /// <summary>
    /// The top entry of one deletion: the file deleted, or the directory whose subtree was. Restoring
    /// puts it back under <see cref="ContainerId"/> with its <see cref="Name"/>.
    /// </summary>
    public class TrashedEntry
    {
        public required Guid DeletionId { get; init; }
        public required TrashEntryKind Kind { get; init; }
        public required Guid EntryId { get; init; }

        /// <summary>The directory the entry was in.</summary>
        public required Guid ContainerId { get; init; }

        public required string Name { get; init; }
        public required DateTimeOffset DeletedAt { get; init; }
        public required int FileCount { get; init; }
        public required long SizeBytes { get; init; }
    }
}
