namespace ByteShelfCommon
{
    /// <summary>A directory on a shelf.</summary>
    public sealed class ShelfDirectory
    {
        /// <summary>The directory's id.</summary>
        public required Guid Id { get; init; }

        /// <summary>The id of the shelf that holds the directory.</summary>
        public required Guid ShelfId { get; init; }

        /// <summary>The id of the directory that holds this one; null for the shelf's root.</summary>
        public Guid? ParentId { get; init; }

        /// <summary>The directory's full path, <c>/</c> for the root.</summary>
        public required string Path { get; init; }

        /// <summary>The directory's name; empty for the root.</summary>
        public required string Name { get; init; }

        /// <summary>When the directory was created.</summary>
        public required DateTimeOffset CreatedAt { get; init; }
    }
}
