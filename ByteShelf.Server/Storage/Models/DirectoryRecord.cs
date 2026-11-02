using System.Text.Json.Serialization;

namespace ByteShelf.Server.Storage.Models
{
    /// <summary>A directory's row. The shelf's root has no parent and an empty name.</summary>
    public class DirectoryRecord
    {
        public required Guid Id { get; init; }
        public required Guid ShelfId { get; init; }
        public Guid? ParentId { get; init; }
        public required string Name { get; init; }
        public required DateTimeOffset CreatedAt { get; init; }
        public required DateTimeOffset UpdatedAt { get; init; }
        /// <summary>
        /// For a directory encrypted with a password, an age file of <see cref="ByteShelfCommon.FileEncryption.CheckText"/>
        /// encrypted with it, so a client can tell a wrong password; null for one that is not encrypted.
        /// </summary>
        public byte[]? EncryptionCheck { get; init; }

        public DateTimeOffset? DeletedAt { get; init; }
        public Guid? DeletionId { get; init; }

        [JsonIgnore]
        public bool IsRoot => ParentId == null;
    }
}
