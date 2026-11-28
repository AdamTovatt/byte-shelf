using System.Text.Json.Serialization;
using ByteShelfCommon.Serialization;

namespace ByteShelfCommon
{
    /// <summary>A stored file: its record on a shelf, without its contents.</summary>
    public sealed class ShelfFile
    {
        /// <summary>The file's id. A file stored again at the same path gets a new one.</summary>
        public required Guid Id { get; init; }

        /// <summary>The id of the shelf that holds the file.</summary>
        public required Guid ShelfId { get; init; }

        /// <summary>The id of the directory that holds the file.</summary>
        public required Guid DirectoryId { get; init; }

        /// <summary>The file's full path on its shelf, e.g. <c>/invoices/2026/march.pdf</c>.</summary>
        public required string Path { get; init; }

        /// <summary>The file's name, the last part of <see cref="Path"/>.</summary>
        public required string Name { get; init; }

        /// <summary>The MIME type given when the file was uploaded.</summary>
        public required string ContentType { get; init; }

        /// <summary>The size of the contents in bytes.</summary>
        public required long SizeBytes { get; init; }

        /// <summary>The lowercase hex SHA-256 of the contents, computed by the server.</summary>
        public required string Sha256 { get; init; }

        /// <summary>When the file was stored.</summary>
        public required DateTimeOffset CreatedAt { get; init; }

        /// <summary>When the file's record last changed.</summary>
        public required DateTimeOffset UpdatedAt { get; init; }

        /// <summary>What the file is, in free text; null when it has none. Search matches it.</summary>
        public string? Description { get; init; }

        /// <summary>
        /// Whether the contents are an age file encrypted with a password (<see cref="FileEncryption"/>):
        /// a client asks for the password and decrypts them; the server never can.
        /// </summary>
        public required bool Encrypted { get; init; }

        /// <summary>
        /// The values of the fields installed plugins declare for listings, by plugin id and field name, for
        /// those the file has. Only a directory's listing, a tree, a search and a deletion's tree carry them;
        /// null everywhere else, and for a file with none.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IReadOnlyDictionary<string, IReadOnlyDictionary<string, PluginFieldValue>>? PluginFields { get; init; }

        /// <summary>
        /// The kinds of derived files there are of the contents, each with a word that changes when another is made
        /// in its place and means nothing else: for a client to ask for nothing that has yet to be made. Only where
        /// <see cref="PluginFields"/> is and in a file's own record; null everywhere else, and for a file with none. Kinds
        /// this client does not know, of a later server, are left out.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonConverter(typeof(KnownEnumKeysConverter<DerivedKind>))]
        public IReadOnlyDictionary<DerivedKind, string>? Derived { get; init; }
    }
}
