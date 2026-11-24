namespace ByteShelf.Server.Metadata.Models
{
    /// <summary>
    /// An <c>object_metadata</c> row: what reading the contents with SHA-256 <see cref="Sha256"/> found,
    /// for every file that holds them and is not encrypted.
    /// </summary>
    public sealed class ObjectMetadata
    {
        /// <summary><see cref="IdFor"/> of <see cref="Sha256"/>.</summary>
        public required Guid Id { get; init; }

        public required string Sha256 { get; init; }

        /// <summary>
        /// The version of <see cref="Services.MetadataReader"/> that read them; 1 for a row, or a primary's
        /// log entry, from before versions were kept.
        /// </summary>
        public int ReaderVersion { get; init; } = 1;

        /// <summary>When the contents were read.</summary>
        public required DateTimeOffset ReadAt { get; init; }

        public required ContentMetadata Metadata { get; init; }

        /// <summary>
        /// The row's id: the first 128 bits of the SHA-256. The change log names an entity by a Guid, and
        /// one made from the contents means a primary and its replicas never hold two rows for one object.
        /// </summary>
        public static Guid IdFor(string sha256)
        {
            return new Guid(Convert.FromHexString(sha256.AsSpan(0, 32)));
        }

        public static ObjectMetadata Of(string sha256, int readerVersion, DateTimeOffset readAt, ContentMetadata metadata)
        {
            return new ObjectMetadata { Id = IdFor(sha256), Sha256 = sha256, ReaderVersion = readerVersion, ReadAt = readAt, Metadata = metadata };
        }
    }
}
