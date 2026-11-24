namespace ByteShelfCommon
{
    /// <summary>A shelf and how much of its quota is in use.</summary>
    public sealed class ShelfInfo
    {
        /// <summary>The shelf's id.</summary>
        public required Guid Id { get; init; }

        /// <summary>The shelf's name, unique on the server and used in its URLs.</summary>
        public required string Name { get; init; }

        /// <summary>How many bytes the shelf may hold; null for no limit.</summary>
        public long? QuotaBytes { get; init; }

        /// <summary>The size of every file on the shelf, the trash included.</summary>
        public required long UsedBytes { get; init; }

        /// <summary>Bytes held back for uploads in progress.</summary>
        public required long ReservedBytes { get; init; }

        /// <summary>How many files are on the shelf, the trash excluded.</summary>
        public required int FileCount { get; init; }

        /// <summary>When the shelf was created.</summary>
        public required DateTimeOffset CreatedAt { get; init; }

        /// <summary>What the caller may do with the shelf's files.</summary>
        public required ShelfPermission Permission { get; init; }

        /// <summary>Whether the server is a replica, which refuses every change to the shelf's files, whatever <see cref="Permission"/> allows.</summary>
        public required bool OnReplica { get; init; }
    }
}
