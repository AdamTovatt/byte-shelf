namespace ByteShelfCommon
{
    /// <summary>
    /// Sets or removes files' descriptions, as <see cref="EntryBatch"/> applies a batch: either
    /// <see cref="Descriptions"/>, each file with its own, or one <see cref="Description"/> (null or blank
    /// removes it) for every file in <see cref="Paths"/>.
    /// </summary>
    public sealed class SetDescriptionsRequest
    {
        /// <summary>Each file's new description, in order; leave <see cref="Paths"/> and <see cref="Description"/> out with these.</summary>
        public IReadOnlyList<DescriptionChange>? Descriptions { get; init; }

        /// <summary>The files that all get <see cref="Description"/>, in order.</summary>
        public IReadOnlyList<string>? Paths { get; init; }

        /// <summary>The description every file in <see cref="Paths"/> gets, at most <see cref="FileDescription.MaxLength"/> characters; null or blank removes theirs.</summary>
        public string? Description { get; init; }

        /// <summary>
        /// The changes in order, every path and description normalized (<see cref="FileDescription.Normalize"/>):
        /// <see cref="Descriptions"/>, or one per path in <see cref="Paths"/> with <see cref="Description"/>.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// Neither form or both is given, there are not 1 to <see cref="EntryBatch.MaxItems"/> items, or a
        /// path or description is not allowed.
        /// </exception>
        public IReadOnlyList<DescriptionChange> Normalize()
        {
            if (Descriptions != null)
            {
                if (Paths != null || Description != null)
                {
                    throw new ArgumentException("Give descriptions, or paths with one description, not both.");
                }

                return EntryBatch.Check(Descriptions, "descriptions")
                    .Select(change => new DescriptionChange { Path = ShelfPath.Normalize(change.Path), Description = FileDescription.Normalize(change.Description) })
                    .ToList();
            }

            if (Paths == null)
            {
                throw new ArgumentException("Give descriptions, or paths with one description.");
            }

            string? description = FileDescription.Normalize(Description);
            return EntryBatch.Check(Paths, "paths").Select(path => new DescriptionChange { Path = ShelfPath.Normalize(path), Description = description }).ToList();
        }
    }
}
